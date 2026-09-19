using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsXos.Services;

/// <summary>
/// One row of a hoswl menu. The same shape serves a top-level menu, a submenu header
/// and a leaf, which is what the protocol asks for: a row with <c>items</c> is a
/// header, a row with <c>sep</c> is a separator, anything else is clickable.
/// </summary>
public sealed class HoswlNode
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("label")] public string? Label { get; init; }
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; init; }
    [JsonPropertyName("check")] public bool? Check { get; init; }
    [JsonPropertyName("sep")] public bool? Sep { get; init; }
    [JsonPropertyName("items")] public List<HoswlNode>? Items { get; init; }

    public static HoswlNode Separator() => new() { Sep = true };

    public static HoswlNode Item(string id, string label, string? key = null,
        bool? check = null, bool enabled = true) =>
        new() { Id = id, Label = label, Key = key, Check = check, Enabled = enabled ? null : false };

    public static HoswlNode Menu(string id, string label, params HoswlNode[] items) =>
        new() { Id = id, Label = label, Items = items.ToList() };
}

/// <summary>
/// Client for hoswl — the Hisashi OS Window Layer protocol. Puts osXos's menus into
/// Hisashi's menubar over <c>\\.\pipe\hoswl</c> and turns the clicks that come back
/// into an event.
///
/// Everything is best-effort and out of the way: Hisashi not running, the pipe
/// breaking mid-session, an old Hisashi that never answers — all of it just means the
/// menus are not there, never that osXos stalls or complains. The connect loop retries
/// quietly in the background for as long as the app lives.
/// </summary>
public sealed class HoswlClient : IAsyncDisposable
{
    const string PipeName = "hoswl";
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    const int ConnectTimeoutMs = 1000;

    static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The default encoder escapes every non-alphanumeric ASCII character, so the
        // plus in a "Ctrl+N" shortcut hint goes out as a unicode escape. Any JSON
        // parser reads that back correctly, but the pipe is not an HTML document and
        // the protocol doc shows plain text, so send plain text.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly string _appId;
    readonly string _appName;
    readonly string _version;
    readonly CancellationTokenSource _stop = new();
    readonly SemaphoreSlim _writeLock = new(1, 1);

    StreamWriter? _writer;
    List<HoswlNode> _menus = new();
    bool _enabled = true;
    Task? _loop;

    /// <summary>Fires on the thread that read it — marshal to the UI yourself.</summary>
    public event Action<string>? Clicked;

    public HoswlClient(string appId, string appName, string version)
    {
        _appId = appId;
        _appName = appName;
        _version = version;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Replaces the whole tree. Safe before or after a connection exists.</summary>
    public void SetMenus(IEnumerable<HoswlNode> menus)
    {
        _menus = menus.ToList();
        _ = SendAsync(new { t = "menu", menus = _menus });
    }

    /// <summary>
    /// The user's own switch. Off keeps the connection but shows nothing in the bar,
    /// which is what the protocol's <c>enable</c> message is for.
    /// </summary>
    public void SetEnabled(bool on)
    {
        _enabled = on;
        _ = SendAsync(new { t = "enable", on });
    }

    /// <summary>Patches one row without resending the tree.</summary>
    public void SetItem(string id, bool? enabled = null, bool? check = null, string? label = null) =>
        _ = SendAsync(new { t = "set", id, enabled, check, label });

    async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SessionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Hisashi is not running, is between accepts, or dropped us. Either
                // way the answer is the same: wait, then try again.
            }

            _writer = null;
            try { await Task.Delay(RetryDelay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task SessionAsync(CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(
            ".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);

        // Byte-mode pipe carrying NDJSON, so plain UTF-8 text without a BOM.
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var reader = new StreamReader(pipe, new UTF8Encoding(false));

        await WriteAsync(writer, new
        {
            t = "hello",
            v = 1,
            app = _appId,
            name = _appName,
            ver = _version,
            pid = Environment.ProcessId,
        }).ConfigureAwait(false);

        _writer = writer;

        // Whatever state the app is in now is the state the bar should show, so the
        // menus and the enable flag go out immediately rather than waiting for a change.
        if (_menus.Count > 0)
            await WriteAsync(writer, new { t = "menu", menus = _menus }).ConfigureAwait(false);
        if (!_enabled)
            await WriteAsync(writer, new { t = "enable", on = false }).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) return;       // pipe closed
            Handle(line);
        }
    }

    void Handle(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (!doc.RootElement.TryGetProperty("t", out var type)) return;

            // "welcome" needs nothing done with it; anything unknown is ignored by
            // design, so the only message worth acting on is a click.
            if (type.GetString() == "click" &&
                doc.RootElement.TryGetProperty("id", out var id) &&
                id.GetString() is { } clicked)
                Clicked?.Invoke(clicked);
        }
        catch
        {
            // A line we cannot parse is Hisashi's problem, not a reason to disconnect.
        }
    }

    async Task SendAsync(object message)
    {
        var writer = _writer;
        if (writer == null) return;         // not connected; the next hello resends state
        try { await WriteAsync(writer, message).ConfigureAwait(false); }
        catch { _writer = null; }
    }

    async Task WriteAsync(StreamWriter writer, object message)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try { await writer.WriteLineAsync(JsonSerializer.Serialize(message, Json)).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await SendAsync(new { t = "bye" }).ConfigureAwait(false);
        _stop.Cancel();
        if (_loop != null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* shutting down */ }
        }
        _stop.Dispose();
        _writeLock.Dispose();
    }
}
