namespace OsXos.Tools.MacOS;

/// <summary>
/// Clears the macOS DNS cache as far as a standard user can. Deliberately does not
/// claim more than that: a full flush also signals mDNSResponder, which needs
/// administrator rights, so the Explain stage says exactly what is and is not covered
/// rather than reporting a clean result for half a job.
/// </summary>
public sealed class FlushDnsTool : ITool
{
    public static readonly ShellCommand Command = new("dscacheutil", "-flushcache");

    /// <summary>The half that needs root. Shown to the user to copy, never run by osXos.</summary>
    public const string ElevatedCommand = "sudo killall -HUP mDNSResponder";

    readonly IProcessRunner _runner;

    public FlushDnsTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.flush-dns";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Network;
    public string Name => "Flush DNS Cache";
    public string Summary => "Clear the Directory Service DNS cache so names resolve fresh.";
    public string IconKey => "IconNetwork";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Why the cache gets in the way",
            "macOS remembers the address behind every hostname it looks up. When a site moves server, or you edit /etc/hosts or change DNS provider, the stale answer keeps being used until it expires by itself."),
        new("Run dscacheutil -flushcache",
            "This empties the Directory Service cache. It runs as your own account — no password prompt, nothing elevated."),
        new("What this does not do",
            "A complete flush on current macOS also signals the mDNSResponder daemon, and that needs administrator rights, which osXos does not ask for. If names still resolve to the old address afterwards, run sudo killall -HUP mDNSResponder in Terminal yourself. The Result stage repeats that line so it is in front of you when it matters."),
        new("Nothing is deleted from disk",
            "The cache lives in memory. There is no file to lose and nothing to undo."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists(Command.File))
        {
            return Task.FromResult(ToolPreview.Blocked(
                "dscacheutil could not be found. That is unexpected on macOS — it lives in /usr/bin."));
        }

        var items = new[]
        {
            new PreviewItem("Will run", Command.Display),
            new PreviewItem("Will NOT run (needs administrator)", ElevatedCommand),
        };
        return Task.FromResult(new ToolPreview(items,
            "Clears the Directory Service cache. The mDNSResponder half needs sudo and is left to you."));
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var outcome = await _runner.RunAsync(Command, ct).ConfigureAwait(false);

        if (!outcome.Ok)
        {
            return ToolResult.Failure("Could not flush the DNS cache",
                Command.Display, $"exit code {outcome.ExitCode}", outcome.Message);
        }

        return ToolResult.Success("Directory Service DNS cache flushed",
            Command.Display,
            "If a name still resolves to its old address, run this in Terminal:",
            ElevatedCommand);
    }
}
