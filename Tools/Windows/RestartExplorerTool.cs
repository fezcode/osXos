namespace OsXos.Tools.Windows;

/// <summary>
/// Restarts the Windows shell on its own, without deleting anything. The everyday
/// fix for a taskbar that has stopped responding, a system tray that has lost its
/// icons, or a desktop that will not redraw.
///
/// The icon cache tool does this as a means to an end; this is the same operation
/// offered as the end in itself, because that is how people actually want it.
/// </summary>
public sealed class RestartExplorerTool : ITool
{
    readonly IShellController _shell;

    public RestartExplorerTool() : this(new ExplorerController()) { }

    public RestartExplorerTool(IShellController shell) => _shell = shell;

    public string Id => "windows.restart-explorer";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Restart Explorer";
    public string Summary => "Restart the Windows shell to fix a stuck taskbar, tray or desktop.";
    public string IconKey => "IconRefresh";
    public string? Warning => "Every open File Explorer window closes. Nothing else is affected — your other applications and their unsaved work are untouched.";
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("What explorer.exe actually is",
            "It is not just the file manager — it draws the taskbar, the Start menu, the system tray and the desktop itself. When any of those stop responding while the rest of Windows is fine, this is the process at fault."),
        new("End it",
            "Every explorer.exe process is ended. The taskbar and desktop disappear for a second, and open folder windows close with them. Applications that are not Explorer keep running and keep their unsaved work — this does not touch them."),
        new("Start it again",
            "osXos waits until the shell is actually running again before reporting success, rather than ending it and hoping. If it does not come back, the Result stage tells you how to start it from Task Manager."),
        new("Nothing is deleted",
            "No file, setting or cache is removed. This is the gentlest thing in osXos: if it does not fix the problem it has cost you a second and changed nothing. To clear the icon cache as well, use Clear Icon Cache instead."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var running = _shell.IsExplorerRunning;

        var items = new[]
        {
            new PreviewItem("Windows Explorer", running
                ? "running — it will be ended and started again"
                : "not running — it will simply be started"),
            new PreviewItem("What closes", "Open File Explorer windows. Other applications are not affected."),
            new PreviewItem("What is deleted", "Nothing."),
        };

        return Task.FromResult(new ToolPreview(items, running
            ? "The shell will restart. Your desktop and taskbar disappear for about a second."
            : "The shell is not running; it will be started."));
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        progress?.Report(new ToolProgress(0, 0, "Closing Windows Explorer..."));
        var stopped = await _shell.StopExplorerAsync(ct).ConfigureAwait(false);

        progress?.Report(new ToolProgress(0, 0, "Starting Windows Explorer..."));
        var restarted = await _shell.StartExplorerAsync(ct).ConfigureAwait(false);

        var lines = new List<string>
        {
            stopped > 0
                ? $"Closed Windows Explorer ({stopped} process{(stopped == 1 ? "" : "es")})."
                : "Windows Explorer was not running.",
        };

        if (!restarted)
        {
            lines.Add("Windows Explorer did not come back. Press Ctrl+Shift+Esc, then File → Run new task → explorer.exe");
            return ToolResult.Failure("Explorer did not restart", lines.ToArray());
        }

        lines.Add("Windows Explorer restarted.");
        return ToolResult.Success("Explorer restarted", lines.ToArray());
    }
}
