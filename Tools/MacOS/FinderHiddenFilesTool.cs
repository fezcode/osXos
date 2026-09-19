namespace OsXos.Tools.MacOS;

/// <summary>
/// Shows or hides dotfiles in Finder, via the same defaults key the command line
/// uses. A two-way toggle: running it again puts Finder back.
/// </summary>
public sealed class FinderHiddenFilesTool : ITool
{
    public static readonly ShellCommand ReadCommand =
        new("defaults", "read", "com.apple.finder", "AppleShowAllFiles");

    public static ShellCommand WriteCommand(bool show) =>
        new("defaults", "write", "com.apple.finder", "AppleShowAllFiles", "-bool", show ? "true" : "false");

    public static readonly ShellCommand KillFinder = new("killall", "Finder");

    readonly IProcessRunner _runner;

    public FinderHiddenFilesTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.finder-hidden-files";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Show Hidden Files in Finder";
    public string Summary => "Toggle Finder between hiding and showing dotfiles and hidden system items.";
    public string IconKey => "IconEye";
    public string? Warning => "Finder restarts, so any open Finder windows close.";
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read what Finder is doing now",
            "defaults read com.apple.finder AppleShowAllFiles. When the key has never been set the command reports an error, which simply means Finder is at its default of hiding them."),
        new("Write the opposite value",
            "defaults write com.apple.finder AppleShowAllFiles -bool true (or false). This is your own user domain — no administrator rights, and no other account on the Mac is affected."),
        new("Restart Finder so it takes effect",
            "killall Finder. macOS relaunches it immediately with the new setting. Open Finder windows are closed by the restart, so finish anything mid-rename first."),
        new("Nothing is created or deleted",
            "Hidden files were always there; this only changes whether Finder draws them. Run the tool again to switch back. Cmd+Shift+. does the same thing in a Finder window if you would rather not use a tool at all."),
    };

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("defaults"))
        {
            return ToolPreview.Blocked(
                "The defaults command could not be found. That is unexpected on macOS — it lives in /usr/bin.");
        }

        var outcome = await _runner.RunAsync(ReadCommand, ct).ConfigureAwait(false);
        // An unset key exits non-zero; that is the default state, not a failure.
        var showing = outcome.Ok && ParseShowing(outcome.StdOut);
        var turningOn = !showing;

        var items = new[]
        {
            new PreviewItem("Hidden files in Finder",
                $"{(showing ? "shown" : "hidden")} → {(turningOn ? "shown" : "hidden")}"),
            new PreviewItem("Will run", WriteCommand(turningOn).Display),
            new PreviewItem("Then run", KillFinder.Display),
        };

        return new ToolPreview(items, turningOn
            ? "Will show dotfiles and hidden items in Finder."
            : "Will hide dotfiles and hidden items in Finder.");
    }

    /// <summary>
    /// defaults prints 1/0 for a bool, but a key written by hand may hold YES or TRUE.
    /// </summary>
    public static bool ParseShowing(string stdout)
    {
        var v = stdout.Trim();
        return v is "1"
            || v.Equals("YES", StringComparison.OrdinalIgnoreCase)
            || v.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct)
    {
        var read = await _runner.RunAsync(ReadCommand, ct).ConfigureAwait(false);
        var turningOn = !(read.Ok && ParseShowing(read.StdOut));

        var write = WriteCommand(turningOn);
        var wrote = await _runner.RunAsync(write, ct).ConfigureAwait(false);
        if (!wrote.Ok)
        {
            return ToolResult.Failure("Could not change the Finder setting",
                write.Display, $"exit code {wrote.ExitCode}", wrote.Message);
        }

        var killed = await _runner.RunAsync(KillFinder, ct).ConfigureAwait(false);

        var lines = new List<string> { write.Display };
        lines.Add(killed.Ok
            ? $"{KillFinder.Display} — Finder restarted."
            : $"{KillFinder.Display} — could not restart Finder ({killed.Message}). Log out and back in for the change to show.");
        lines.Add("Run this tool again to switch back.");

        return ToolResult.Success(
            turningOn ? "Finder now shows hidden files" : "Finder now hides hidden files", lines.ToArray());
    }
}
