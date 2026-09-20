namespace OsXos.Tools.Linux;

/// <summary>
/// Flushes the systemd-resolved DNS cache. Linux has no single DNS cache, so this
/// tool checks which resolver is actually in use and blocks with a specific
/// explanation rather than running a command that would silently do nothing.
/// </summary>
public sealed class FlushDnsTool : ITool
{
    public const string Binary = "resolvectl";

    public static readonly ShellCommand Command = new(Binary, "flush-caches");

    /// <summary>Used only to name the resolver in the preview; never acted on.</summary>
    public static readonly ShellCommand StatusCommand = new(Binary, "status");

    readonly IProcessRunner _runner;

    public FlushDnsTool(IProcessRunner runner) => _runner = runner;

    public string Id => "linux.flush-dns";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Network;
    public string Name => "Flush DNS Cache";
    public string Summary => "Clear the systemd-resolved cache so names resolve fresh.";
    public string IconKey => "IconNetwork";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Linux has no single DNS cache",
            "Which cache to clear depends entirely on what is resolving names on this machine: systemd-resolved, dnsmasq, nscd, unbound, or nothing at all — in which case every lookup already goes straight out and there is nothing to flush."),
        new("Check for systemd-resolved",
            "osXos looks for resolvectl, the control tool for systemd-resolved, which is the default on Debian, Ubuntu, Fedora and Arch with NetworkManager. If it is not there, the Review stage tells you so and refuses to run rather than pretending a different resolver was cleared."),
        new("Run resolvectl flush-caches",
            "This empties the cache for every link. It goes through the system bus, and the default polkit policy lets a locally logged-in user do it without a password."),
        new("Nothing is deleted from disk",
            "The cache lives in memory inside the resolver. There is no file to lose and nothing to undo; the next lookup for each name is answered by your DNS server again."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists(Binary))
        {
            return Task.FromResult(ToolPreview.Blocked(
                "systemd-resolved does not appear to be in use — resolvectl is not installed. If this machine caches DNS at all it is doing so through something else (dnsmasq, nscd or unbound), each of which is cleared differently, so osXos will not guess."));
        }

        var items = new[] { new PreviewItem("Will run", Command.Display) };
        return Task.FromResult(new ToolPreview(items,
            "Clears the systemd-resolved cache on every network link."));
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var outcome = await _runner.RunAsync(Command, ct).ConfigureAwait(false);

        if (!outcome.Ok)
        {
            return ToolResult.Failure("Could not flush the DNS cache",
                Command.Display,
                $"exit code {outcome.ExitCode}",
                outcome.Message);
        }

        return ToolResult.Success("DNS cache flushed",
            Command.Display,
            string.IsNullOrWhiteSpace(outcome.Message)
                ? "systemd-resolved is no longer holding any cached answers."
                : outcome.Message);
    }
}
