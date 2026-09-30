namespace OsXos.Tools.MacOS;

/// <summary>
/// Deletes the simulator devices Xcode can no longer run — ones whose runtime was
/// removed by an Xcode update. Each carries its own disk image and app data, and
/// Xcode never cleans them up by itself.
/// </summary>
public sealed class UnavailableSimulatorsTool : ITool
{
    public static readonly ShellCommand ListCommand = new("xcrun", "simctl", "list", "devices", "unavailable");
    public static readonly ShellCommand DeleteCommand = new("xcrun", "simctl", "delete", "unavailable");

    readonly IProcessRunner _runner;

    public UnavailableSimulatorsTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.unavailable-simulators";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Developer;
    public string Name => "Delete Unavailable Simulators";
    public string Summary => "Remove simulator devices whose runtime is gone, with the disk space they still hold.";
    public string IconKey => "IconDeveloper";
    public string? Warning => "Apps and data installed on those simulators go with them. They cannot be booted any more, so nothing usable is lost.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Ask Xcode which simulators are dead",
            "xcrun simctl list devices unavailable. A simulator becomes unavailable when the iOS, watchOS, tvOS or visionOS runtime it was made for is removed — usually by an Xcode update."),
        new("List them before anything is removed",
            "The Review stage names every one. Working simulators are never listed, because simctl never reports them as unavailable."),
        new("Delete them the supported way",
            "xcrun simctl delete unavailable — Apple's own command for exactly this, which removes each device's disk image and data together."),
        new("Nothing else changes",
            "Runtimes, working simulators and Xcode itself are untouched. Clear Xcode Build Data handles DerivedData and device support files."),
    };

    /// <summary>Device lines in simctl's listing carry "(unavailable" after the state.</summary>
    public static IReadOnlyList<string> ParseUnavailable(string stdout) =>
        stdout.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains("(unavailable", StringComparison.Ordinal))
            .ToList();

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("xcrun"))
            return ToolPreview.Blocked("Xcode's command line tools are not installed, so there are no simulators to check.");

        var listed = await _runner.RunAsync(ListCommand, ct).ConfigureAwait(false);
        if (!listed.Ok)
            return ToolPreview.Blocked($"simctl could not list simulators: {listed.Message}");

        var devices = ParseUnavailable(listed.StdOut);
        if (devices.Count == 0)
            return ToolPreview.Blocked("Nothing to delete — every simulator on this Mac still has its runtime.");

        var items = devices.Select(d => new PreviewItem(d.Split(" (")[0], d))
            .Append(new PreviewItem("Will run", DeleteCommand.Display))
            .ToList();
        return new ToolPreview(items, $"{devices.Count} unavailable simulator{(devices.Count == 1 ? "" : "s")} to delete.");
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var outcome = await _runner.RunAsync(DeleteCommand, ct).ConfigureAwait(false);
        return outcome.Ok
            ? ToolResult.Success("Unavailable simulators deleted", DeleteCommand.Display)
            : ToolResult.Failure("Could not delete the simulators",
                DeleteCommand.Display, $"exit code {outcome.ExitCode}", outcome.Message);
    }
}

/// <summary>
/// Resets Quick Look's thumbnail cache — the fix for Finder showing a stale or blank
/// preview after a file changed.
/// </summary>
public sealed class QuickLookCacheTool : ITool
{
    public static readonly ShellCommand ResetCommand = new("qlmanage", "-r", "cache");

    readonly IProcessRunner _runner;

    public QuickLookCacheTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.quicklook-cache";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Reset Quick Look Cache";
    public string Summary => "Clear Finder's preview thumbnails so stale or blank ones are drawn again.";
    public string IconKey => "IconEye";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("What the cache is",
            "Quick Look draws the thumbnails Finder shows in icon and gallery view and the previews behind the space bar, and keeps them so they are not rendered twice. A file edited in place can keep showing its old picture."),
        new("Reset it",
            "qlmanage -r cache — Apple's own tool, telling Quick Look to throw its thumbnail cache away. No administrator rights, and only your account's cache."),
        new("Finder redraws as it goes",
            "Thumbnails are rendered again the next time each folder is opened, which is a moment slower the first time. Your files are not touched."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("qlmanage"))
            return Task.FromResult(ToolPreview.Blocked("qlmanage could not be found. That is unexpected on macOS — it lives in /usr/bin."));

        return Task.FromResult(new ToolPreview(
            new[] { new PreviewItem("Will run", ResetCommand.Display) },
            "Will reset Quick Look's thumbnail cache."));
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var outcome = await _runner.RunAsync(ResetCommand, ct).ConfigureAwait(false);
        return outcome.Ok
            ? ToolResult.Success("Quick Look cache reset", ResetCommand.Display,
                "Thumbnails are drawn again the next time each folder is opened.")
            : ToolResult.Failure("Could not reset Quick Look", ResetCommand.Display,
                $"exit code {outcome.ExitCode}", outcome.Message);
    }
}

/// <summary>Restarts the Dock — for a frozen Dock, a stuck Mission Control or Launchpad.</summary>
public sealed class RestartDockTool : ITool
{
    public static readonly ShellCommand KillDock = new("killall", "Dock");

    readonly IProcessRunner _runner;

    public RestartDockTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.restart-dock";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Restart Dock";
    public string Summary => "Restart the Dock to fix a frozen Dock, Mission Control or Launchpad.";
    public string IconKey => "IconRefresh";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("What the Dock process draws",
            "Not just the Dock: Mission Control, Launchpad, the desktop spaces and app switching all belong to it. When those stop responding while apps are fine, this is the process at fault."),
        new("End it",
            "killall Dock. macOS relaunches it straight away, reading the same settings as before."),
        new("Nothing is lost",
            "Open windows and apps are not affected, and nothing is deleted. The Dock disappears for a moment and comes back."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct) =>
        Task.FromResult(new ToolPreview(
            new[]
            {
                new PreviewItem("Will run", KillDock.Display),
                new PreviewItem("What closes", "Nothing — the Dock restarts itself immediately."),
            },
            "The Dock will restart."));

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var outcome = await _runner.RunAsync(KillDock, ct).ConfigureAwait(false);
        return outcome.Ok
            ? ToolResult.Success("The Dock restarted", KillDock.Display)
            : ToolResult.Failure("Could not restart the Dock", KillDock.Display,
                $"exit code {outcome.ExitCode}", outcome.Message);
    }
}

/// <summary>
/// Shows or hides Finder's path bar and status bar together. The path bar is where
/// you are; the status bar is how many items and how much space is free.
/// </summary>
public sealed class FinderBarsTool : ITool, IHasState
{
    public static ShellCommand Read(string key) => new("defaults", "read", "com.apple.finder", key);

    public static ShellCommand Write(string key, bool on) =>
        new("defaults", "write", "com.apple.finder", key, "-bool", on ? "true" : "false");

    public static readonly ShellCommand KillFinder = new("killall", "Finder");

    readonly IProcessRunner _runner;

    public FinderBarsTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.finder-bars";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Show Finder Path & Status Bars";
    public string Summary => "Toggle Finder's path bar and status bar on or off together.";
    public string IconKey => "IconFolder";
    public string? Warning => "Finder restarts, so any open Finder windows close.";
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read both settings",
            "defaults read com.apple.finder ShowPathbar and ShowStatusBar. A key that was never set is off, Finder's default."),
        new("Turn both on, or both off",
            "If either is off, both are turned on; if both are already on, both are turned off. Run it twice and you are back where you started."),
        new("Restart Finder",
            "killall Finder, so it redraws with the new setting. Open Finder windows close with it."),
    };

    public async Task<ToolState> ReadStateAsync(CancellationToken ct) =>
        await BothOn(ct).ConfigureAwait(false) ? new ToolState("Shown", StateTone.On) : new ToolState("Hidden", StateTone.Off);

    async Task<bool> BothOn(CancellationToken ct) =>
        FinderHiddenFilesTool.ParseShowing((await _runner.RunAsync(Read("ShowPathbar"), ct).ConfigureAwait(false)).StdOut) &&
        FinderHiddenFilesTool.ParseShowing((await _runner.RunAsync(Read("ShowStatusBar"), ct).ConfigureAwait(false)).StdOut);

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("defaults"))
            return ToolPreview.Blocked("The defaults command could not be found. That is unexpected on macOS — it lives in /usr/bin.");

        var on = await BothOn(ct).ConfigureAwait(false);
        var items = new[]
        {
            new PreviewItem("Path bar and status bar", on ? "shown → hidden" : "hidden → shown"),
            new PreviewItem("Will run", Write("ShowPathbar", !on).Display),
            new PreviewItem("Will run", Write("ShowStatusBar", !on).Display),
            new PreviewItem("Then run", KillFinder.Display),
        };
        return new ToolPreview(items, on ? "Will hide Finder's path and status bars." : "Will show Finder's path and status bars.");
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var target = !await BothOn(ct).ConfigureAwait(false);
        foreach (var key in new[] { "ShowPathbar", "ShowStatusBar" })
        {
            var write = Write(key, target);
            var outcome = await _runner.RunAsync(write, ct).ConfigureAwait(false);
            if (!outcome.Ok)
                return ToolResult.Failure("Could not change the Finder setting",
                    write.Display, $"exit code {outcome.ExitCode}", outcome.Message);
        }

        await _runner.RunAsync(KillFinder, ct).ConfigureAwait(false);
        return ToolResult.Success(
            target ? "Finder now shows its path and status bars" : "Finder's path and status bars are hidden",
            "Finder restarted.", "Run this tool again to switch back.");
    }
}

/// <summary>
/// Moves where screenshots are saved between the Desktop — macOS's default — and a
/// Screenshots folder in Pictures, so a week of screen grabs stops burying the desktop.
/// </summary>
public sealed class ScreenshotFolderTool : ITool, IHasState
{
    public static readonly ShellCommand ReadCommand = new("defaults", "read", "com.apple.screencapture", "location");
    public static ShellCommand WriteCommand(string path) => new("defaults", "write", "com.apple.screencapture", "location", path);
    public static readonly ShellCommand ResetCommand = new("defaults", "delete", "com.apple.screencapture", "location");
    public static readonly ShellCommand RestartUi = new("killall", "SystemUIServer");

    readonly IProcessRunner _runner;
    readonly string _home;

    public ScreenshotFolderTool(IProcessRunner runner)
        : this(runner, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    public ScreenshotFolderTool(IProcessRunner runner, string home)
    {
        _runner = runner;
        _home = home;
    }

    public string Folder => Path.Combine(_home, "Pictures", "Screenshots");

    public string Id => "macos.screenshot-folder";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Save Screenshots to Pictures";
    public string Summary => "Toggle where screenshots land: a Screenshots folder in Pictures, or back on the Desktop.";
    public string IconKey => "IconImage";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read where screenshots go now",
            "defaults read com.apple.screencapture location. With no value set they go to the Desktop, macOS's default."),
        new("Switch to the other place",
            "From the Desktop (or anywhere else) they move to ~/Pictures/Screenshots, which is created if it does not exist. From that folder they move back to the Desktop by removing the setting, exactly as a fresh Mac has it."),
        new("Apply it",
            "killall SystemUIServer, so the screenshot tool picks the new location up. The menu bar blinks for a moment."),
        new("Existing screenshots stay put",
            "Nothing is moved or deleted — only where new ones are saved changes. Run the tool again to switch back."),
    };

    public async Task<ToolState> ReadStateAsync(CancellationToken ct) =>
        IsFolder(await Current(ct).ConfigureAwait(false))
            ? new ToolState("Pictures", StateTone.On, Folder)
            : new ToolState("Desktop", StateTone.Off);

    async Task<string?> Current(CancellationToken ct)
    {
        var read = await _runner.RunAsync(ReadCommand, ct).ConfigureAwait(false);
        return read.Ok && read.StdOut.Trim().Length > 0 ? read.StdOut.Trim() : null;
    }

    bool IsFolder(string? current) =>
        current is not null &&
        Path.TrimEndingDirectorySeparator(current.Replace("~", _home)) == Path.TrimEndingDirectorySeparator(Folder);

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("defaults"))
            return ToolPreview.Blocked("The defaults command could not be found. That is unexpected on macOS — it lives in /usr/bin.");

        var current = await Current(ct).ConfigureAwait(false);
        var toDesktop = IsFolder(current);
        var items = new[]
        {
            new PreviewItem("Screenshots are saved to",
                $"{current ?? "Desktop (default)"} → {(toDesktop ? "Desktop (default)" : Folder)}"),
            new PreviewItem("Will run", toDesktop ? ResetCommand.Display : WriteCommand(Folder).Display),
            new PreviewItem("Then run", RestartUi.Display),
        };
        return new ToolPreview(items, toDesktop
            ? "Will save screenshots to the Desktop again."
            : "Will save screenshots to Pictures/Screenshots.");
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var toDesktop = IsFolder(await Current(ct).ConfigureAwait(false));
        ShellCommand command;

        if (toDesktop)
        {
            command = ResetCommand;
        }
        else
        {
            try { Directory.CreateDirectory(Folder); }
            catch (Exception ex) { return ToolResult.Failure("Could not create the Screenshots folder", Folder, ex.Message); }
            command = WriteCommand(Folder);
        }

        var outcome = await _runner.RunAsync(command, ct).ConfigureAwait(false);
        if (!outcome.Ok)
            return ToolResult.Failure("Could not change the screenshot location",
                command.Display, $"exit code {outcome.ExitCode}", outcome.Message);

        await _runner.RunAsync(RestartUi, ct).ConfigureAwait(false);
        return ToolResult.Success(
            toDesktop ? "Screenshots are saved to the Desktop" : "Screenshots are saved to Pictures/Screenshots",
            command.Display, "Existing screenshots were not moved.", "Run this tool again to switch back.");
    }
}

/// <summary>
/// Empties the Trash through Finder. macOS guards ~/.Trash behind Full Disk Access,
/// so rather than ask for access to everything, osXos asks Finder — which owns the
/// Trash — to count it and to empty it, exactly as the Finder menu item does.
/// </summary>
public sealed class MacEmptyTrashTool : ITool
{
    public static readonly ShellCommand CountCommand =
        new("osascript", "-e", "tell application \"Finder\" to count (items of trash)");

    public static readonly ShellCommand EmptyCommand =
        new("osascript", "-e", "tell application \"Finder\" to empty trash");

    readonly IProcessRunner _runner;

    public MacEmptyTrashTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.empty-trash";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Empty Trash";
    public string Summary => "Permanently delete what is in the Trash, after showing how many items it holds.";
    public string IconKey => "IconTrash";

    public string? Warning =>
        "Emptied is gone. The first time, macOS asks whether osXos may control Finder — allow it, or the Trash cannot be emptied.";

    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Ask Finder what is in the Trash",
            "macOS protects the Trash folder from other apps unless they are granted Full Disk Access. Rather than ask for that, osXos asks Finder, which owns the Trash, to count it."),
        new("Allow Automation once",
            "macOS asks, the first time, whether osXos may control Finder. The answer is remembered under System Settings → Privacy & Security → Automation."),
        new("Empty it the way Finder does",
            "The same command as Finder → Empty Trash, including the Trash folders on other connected drives."),
    };

    static bool Refused(ProcessOutcome o) => o.StdErr.Contains("-1743", StringComparison.Ordinal);

    const string RefusedText =
        "osXos is not allowed to control Finder. Turn it on under System Settings → Privacy & Security → Automation, then run this again.";

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("osascript"))
            return ToolPreview.Blocked("osascript could not be found. That is unexpected on macOS — it lives in /usr/bin.");

        var counted = await _runner.RunAsync(CountCommand, ct).ConfigureAwait(false);
        if (Refused(counted)) return ToolPreview.Blocked(RefusedText);
        if (!counted.Ok) return ToolPreview.Blocked($"Finder could not count the Trash: {counted.Message}");

        if (!int.TryParse(counted.StdOut.Trim(), out var count) || count == 0)
            return ToolPreview.Blocked("The Trash is already empty.");

        return new ToolPreview(
            new[]
            {
                new PreviewItem("Items in the Trash", count.ToString("N0")),
                new PreviewItem("Will run", EmptyCommand.Display),
            },
            $"{count:N0} item{(count == 1 ? "" : "s")} will be permanently deleted.");
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var outcome = await _runner.RunAsync(EmptyCommand, ct).ConfigureAwait(false);
        if (Refused(outcome)) return ToolResult.Failure("macOS did not allow it", RefusedText);
        return outcome.Ok
            ? ToolResult.Success("The Trash is empty", EmptyCommand.Display)
            : ToolResult.Failure("Could not empty the Trash", EmptyCommand.Display,
                $"exit code {outcome.ExitCode}", outcome.Message);
    }
}
