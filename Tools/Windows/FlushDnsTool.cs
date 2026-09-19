namespace OsXos.Tools.Windows;

/// <summary>
/// Clears the Windows DNS resolver cache. The one tool here that needs no warning:
/// nothing is deleted from disk and the cache refills itself on the next lookup.
/// </summary>
public sealed class FlushDnsTool : ITool
{
    /// <summary>
    /// Exposed so a test can assert the exact argv. <c>ipconfig /flushdns</c> works
    /// for a standard user; it is clearing the resolver cache, not reconfiguring it.
    /// </summary>
    public static readonly ShellCommand Command = new("ipconfig", "/flushdns");

    readonly IProcessRunner _runner;

    public FlushDnsTool(IProcessRunner runner) => _runner = runner;

    public string Id => "windows.flush-dns";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Network;
    public string Name => "Flush DNS Cache";
    public string Summary => "Discard remembered DNS lookups so names resolve fresh.";
    public string IconKey => "IconNetwork";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Why the cache gets in the way",
            "Windows remembers the address behind every hostname it looks up, for as long as that record says to. When a site moves server, or you change your hosts file or your DNS provider, the old answer keeps being used until it expires on its own."),
        new("Run ipconfig /flushdns",
            "This asks the DNS Client service to drop every entry it is holding. It runs as your own account — no administrator rights, no elevation prompt."),
        new("Nothing is deleted from disk",
            "The cache lives in memory only. There is no file to lose and nothing to undo."),
        new("The next lookup is authoritative",
            "The first request for each name after this goes out to your DNS server again, so you get the current answer. A handful of page loads may feel marginally slower for a moment."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists(Command.File))
        {
            return Task.FromResult(ToolPreview.Blocked(
                "ipconfig could not be found on this system. That is unexpected on Windows — check that %SystemRoot%\\System32 is on your PATH."));
        }

        var items = new[] { new PreviewItem("Command", Command.Display) };
        return Task.FromResult(new ToolPreview(items, "Clears every cached DNS entry for this machine."));
    }

    public async Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct)
    {
        var outcome = await _runner.RunAsync(Command, ct).ConfigureAwait(false);

        if (!outcome.Ok)
        {
            return ToolResult.Failure(
                "Could not flush the DNS cache",
                Command.Display,
                $"exit code {outcome.ExitCode}",
                outcome.Message);
        }

        return ToolResult.Success(
            "DNS cache flushed",
            Command.Display,
            string.IsNullOrWhiteSpace(outcome.Message)
                ? "The resolver cache is now empty."
                : outcome.Message);
    }
}
