namespace OsXos.Tools.Windows;

/// <summary>
/// Hides the Windows taskbar for good — for a desktop where Hisashi's menubar and
/// dock do its job. Windows has no setting for this, so it is three things together:
/// auto-hide on (Windows' own, persistent setting), the taskbar window hidden
/// outright so it does not slide back at the screen edge, and a small keeper that
/// hides every new taskbar Explorer makes — after a restart, an Explorer crash, or a
/// monitor being plugged in — started now and again at every sign-in.
///
/// Running it again undoes all three and puts auto-hide back the way it was.
/// </summary>
public sealed class HideTaskbarTool : ITool, IHasState
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "osXos Hide Taskbar";

    /// <summary>Where osXos remembers whether auto-hide was already on before it hid the taskbar.</summary>
    public const string StateKey = @"Software\fezcode\osxos";
    public const string AutoHideBeforeValue = "TaskbarAutoHideBefore";

    readonly ITaskbarController _taskbar;
    readonly IUserRegistry _registry;
    readonly ITaskbarKeeperProcess _keeper;
    readonly Func<string?> _exePath;
    readonly Func<bool> _hisashiRunning;

    public HideTaskbarTool(ITaskbarController taskbar, IUserRegistry registry, ITaskbarKeeperProcess keeper)
        : this(taskbar, registry, keeper,
            () => Environment.ProcessPath,
            () => System.Diagnostics.Process.GetProcessesByName("hisashi").Length > 0) { }

    public HideTaskbarTool(
        ITaskbarController taskbar, IUserRegistry registry, ITaskbarKeeperProcess keeper,
        Func<string?> exePath, Func<bool> hisashiRunning)
    {
        _taskbar = taskbar;
        _registry = registry;
        _keeper = keeper;
        _exePath = exePath;
        _hisashiRunning = hisashiRunning;
    }

    public string Id => "windows.hide-taskbar";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Hide the Taskbar";
    public string Summary => "Hide the Windows taskbar permanently — for a desktop run from Hisashi's menubar and dock — or bring it back.";
    public string IconKey => "IconMinimize";

    public string? Warning =>
        "The taskbar stays gone — through restarts, Explorer restarts and new monitors — until you run this tool again. " +
        "The Windows key still opens Start, Alt+Tab still switches windows, and Win+A and Win+N still open quick settings and notifications.";

    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Turn on auto-hide",
            "Windows' own \"Automatically hide the taskbar\" setting, set through the documented appbar API so it applies straight away and Windows saves it. Windows hands the taskbar's strip of screen back to your windows."),
        new("Hide the taskbar outright",
            "An auto-hidden taskbar still slides up when the pointer touches the bottom edge. osXos hides the taskbar window itself — and the one on each extra monitor — so it does not come back at all."),
        new("Keep it hidden",
            "Every Explorer restart, and every monitor plugged in, makes a new taskbar window. A small osXos keeper with no window watches for new ones and hides them within a second; it never re-hides one it already hid, so it cannot fight you or Windows. It starts now, and again at every sign-in from an entry named \"osXos Hide Taskbar\" — which Startup Apps Report and Task Manager both show."),
        new("Run it again to undo everything",
            "The keeper is told to stop, the sign-in entry removed, the taskbar shown on every monitor, and auto-hide put back the way it was before — off, unless you had it on. No administrator rights at any point; only your own account's taskbar is affected."),
    };

    bool SignInEntryPresent => _registry.GetString(RunKey, RunValue) is not null;

    /// <summary>On means osXos is keeping the taskbar hidden: the entry is there and the taskbar is hidden.</summary>
    bool HiddenByOsXos => SignInEntryPresent && _taskbar.IsHidden;

    public Task<ToolState> ReadStateAsync(CancellationToken ct) => Task.FromResult(
        !_taskbar.Exists ? new ToolState("Not running", StateTone.Unavailable, "Explorer's taskbar is not running.")
        : HiddenByOsXos ? new ToolState("Hidden", StateTone.On, _keeper.IsRunning ? "Kept hidden by osXos." : "Hidden, but the keeper is not running.")
        : SignInEntryPresent ? new ToolState("Showing again", StateTone.Partial, "Set to hide, but the taskbar is showing.")
        : new ToolState("Shown", StateTone.Off));

    public static string SignInCommand(string exe) => $"\"{exe}\" {TaskbarKeeper.Argument}";

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_taskbar.Exists)
            return Task.FromResult(ToolPreview.Blocked(
                "The Windows taskbar is not running — Explorer may be stopped. Restart Explorer first, then run this again."));

        if (HiddenByOsXos)
        {
            var before = _registry.GetDword(StateKey, AutoHideBeforeValue) is 1;
            return Task.FromResult(new ToolPreview(new[]
            {
                new PreviewItem("Taskbar", "hidden → shown"),
                new PreviewItem("Keeper", _keeper.IsRunning ? "running → stopped" : "not running"),
                new PreviewItem("Auto-hide", $"on → {(before ? "on (as you had it)" : "off (as you had it)")}"),
                new PreviewItem("Sign-in entry", $"\"{RunValue}\" → removed"),
            }, "Will bring the taskbar back and stop keeping it hidden."));
        }

        var exe = _exePath();
        if (string.IsNullOrEmpty(exe))
            return Task.FromResult(ToolPreview.Blocked(
                "osXos could not work out where its own program file is, so it cannot start the keeper that holds the taskbar hidden."));

        var repairing = SignInEntryPresent;
        var items = new List<PreviewItem>
        {
            new("Taskbar", repairing ? "shown again → hidden" : "shown → hidden"),
            new("Auto-hide", _taskbar.AutoHide ? "already on" : "off → on"),
            new("Keeper", _keeper.IsRunning ? "already running" : "starts now, and at every sign-in"),
            new("Sign-in entry", repairing ? $"\"{RunValue}\" — already there" : $"\"{RunValue}\" → {SignInCommand(exe)}"),
            _hisashiRunning()
                ? new PreviewItem("Hisashi", "running — its menubar and dock take over from the taskbar")
                : new PreviewItem("Hisashi", "not running — with no taskbar, use the Windows key for Start and Alt+Tab to switch windows"),
        };

        return Task.FromResult(new ToolPreview(items, repairing
            ? "The taskbar is showing although osXos is set to hide it. Will hide it again."
            : "Will hide the taskbar, and keep it hidden."));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        try
        {
            return Task.FromResult(HiddenByOsXos ? Show() : Hide());
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure("Could not change the taskbar", ex.Message));
        }
    }

    ToolResult Hide()
    {
        var exe = _exePath();
        if (string.IsNullOrEmpty(exe))
            return ToolResult.Failure("Could not find osXos's own program file", "Nothing was changed.");

        // Remember auto-hide only the first time; a repair must not record the "on"
        // osXos itself set as though it were the user's own choice.
        if (!SignInEntryPresent)
            _registry.SetDword(StateKey, AutoHideBeforeValue, _taskbar.AutoHide ? 1 : 0);

        _taskbar.AutoHide = true;
        var windows = _taskbar.SetVisible(false);
        _registry.SetString(RunKey, RunValue, SignInCommand(exe));
        _keeper.Start(exe);

        return ToolResult.Success("The taskbar is hidden",
            $"Hidden {windows} taskbar window{(windows == 1 ? "" : "s")}, with auto-hide on so the space is yours.",
            "The keeper is running, and starts again at every sign-in, so the taskbar stays hidden through Explorer restarts and new monitors.",
            "Windows key: Start. Alt+Tab: switch windows. Win+A / Win+N: quick settings and notifications.",
            "Run this tool again to bring the taskbar back.");
    }

    ToolResult Show()
    {
        var before = _registry.GetDword(StateKey, AutoHideBeforeValue) is 1;

        // The keeper goes first, so it cannot hide the taskbar again as it reappears.
        _keeper.Stop();
        _registry.DeleteValue(RunKey, RunValue);
        _taskbar.SetVisible(true);
        _taskbar.AutoHide = before;
        _registry.DeleteValue(StateKey, AutoHideBeforeValue);

        return ToolResult.Success("The taskbar is back",
            "The keeper was stopped and the sign-in entry removed.",
            "Taskbar shown on every monitor.",
            $"Auto-hide {(before ? "left on, as you had it" : "turned off again, as you had it")}.");
    }
}
