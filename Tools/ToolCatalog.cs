using OsXos.Tools.Ai;
using OsXos.Tools.Sweeps;
using OsXos.Tools.Windows;

namespace OsXos.Tools;

/// <summary>
/// Every tool osXos ships, grouped by the OS it belongs to. Building a platform's set
/// takes its dependencies as arguments rather than reaching for the real ones, which
/// is what lets the tests construct all three catalogues on one machine — the macOS
/// and Linux tools can be checked for well-formedness here even though they can only
/// actually run there.
///
/// Adding a tool is one entry in the relevant method.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ITool> Windows(
        IProcessRunner runner,
        IExplorerAdvancedSettings explorer,
        IRegistryAccess? registry = null,
        IRecycleBin? recycleBin = null,
        IElevationService? elevation = null,
        IShellController? shell = null,
        IWindowsAppearance? appearance = null,
        IUserRegistry? userRegistry = null,
        ITaskbarController? taskbar = null,
        ITaskbarKeeperProcess? keeper = null)
    {
        registry ??= CreateRegistry();
        appearance ??= CreateAppearance();
        userRegistry ??= CreateUserRegistry();
        taskbar ??= CreateTaskbar();
        keeper ??= CreateKeeper();
        recycleBin ??= CreateRecycleBin();
        elevation ??= new ElevationService(runner);
        shell ??= new ExplorerController();

        return new ITool[]
        {
            // Maintenance
            new IconCacheTool(),
            new TempFolderTool(),
            new RecycleBinTool(recycleBin),
            new UpdateCacheTool(elevation),
            new LocationCleanupTool(PlatformSweeps.ShaderCaches),
            new LocationCleanupTool(PlatformSweeps.CrashReports),

            // Explorer & Shell
            new HiddenFilesTool(explorer),
            new RestartExplorerTool(shell),
            new OpenWithCacheTool(registry, shell),
            new ClassicContextMenuTool(userRegistry, shell),
            new ExplorerToggleTool(ExplorerToggleTool.ClockSeconds, userRegistry),
            new ExplorerToggleTool(ExplorerToggleTool.FullPathTitle, userRegistry),
            new HideTaskbarTool(taskbar, userRegistry, keeper),

            // System
            new Tools.Windows.DarkModeTool(appearance),
            new StartupAppsTool(registry),

            // Network
            new Tools.Windows.FlushDnsTool(runner),

            // Privacy
            new RecentItemsTool(),
            new ExplorerHistoryTool(registry),
            new LocationCleanupTool(BrowserCaches.For(OSKind.Windows)),

            // Developer
            new DeveloperStatusTool(registry),
            new PathHealthTool(),
            new LocationCleanupTool(DeveloperCaches.For(OSKind.Windows)),

            // AI Assistants
            new AiCleanupTool(OSKind.Windows, AiJob.Temp),
            new AiCleanupTool(OSKind.Windows, AiJob.Caches),
            new AiCleanupTool(OSKind.Windows, AiJob.History),
            new AiCleanupTool(OSKind.Windows, AiJob.Everything),
        };
    }

    public static IReadOnlyList<ITool> MacOS(IProcessRunner runner) =>
        new ITool[]
        {
            // Maintenance
            new Tools.MacOS.IconServicesCacheTool(runner),
            new Tools.MacOS.UserCachesTool(),
            new Tools.MacOS.MacEmptyTrashTool(runner),
            new Tools.MacOS.QuickLookCacheTool(runner),

            // Finder & Dock
            new Tools.MacOS.FinderHiddenFilesTool(runner),
            new Tools.MacOS.FinderBarsTool(runner),
            new Tools.MacOS.RestartDockTool(runner),
            new Tools.MacOS.ScreenshotFolderTool(runner),

            // System
            new Tools.MacOS.DarkModeTool(runner),

            // Network
            new Tools.MacOS.FlushDnsTool(runner),

            // Privacy
            new LocationCleanupTool(BrowserCaches.For(OSKind.MacOS)),

            // Developer
            new LocationCleanupTool(DeveloperCaches.For(OSKind.MacOS)),
            new LocationCleanupTool(PlatformSweeps.XcodeData),
            new Tools.MacOS.UnavailableSimulatorsTool(runner),

            // AI Assistants

            new AiCleanupTool(OSKind.MacOS, AiJob.Temp),
            new AiCleanupTool(OSKind.MacOS, AiJob.Caches),
            new AiCleanupTool(OSKind.MacOS, AiJob.History),
            new AiCleanupTool(OSKind.MacOS, AiJob.Everything),
        };

    public static IReadOnlyList<ITool> Linux(IProcessRunner runner) =>
        new ITool[]
        {
            // Maintenance
            new Tools.Linux.ThumbnailCacheTool(),
            new Tools.Linux.UserCacheTool(),
            new LocationCleanupTool(PlatformSweeps.LinuxTrash),
            new LocationCleanupTool(BrowserCaches.For(OSKind.Linux)),

            // Desktop & Shell
            new Tools.Linux.IconCacheRebuildTool(runner),

            // System
            new Tools.Linux.DarkModeTool(runner),

            // Network
            new Tools.Linux.FlushDnsTool(runner),

            // Packages
            new LocationCleanupTool(DeveloperCaches.For(OSKind.Linux)),
            new Tools.Linux.FlatpakUnusedTool(runner),
            new Tools.Linux.PackageCacheTool(runner, new ElevationService(runner)),

            // Services
            new Tools.Linux.RestartAudioTool(runner),
            new Tools.Linux.FailedServicesTool(runner),

            // AI Assistants

            new AiCleanupTool(OSKind.Linux, AiJob.Temp),
            new AiCleanupTool(OSKind.Linux, AiJob.Caches),
            new AiCleanupTool(OSKind.Linux, AiJob.History),
            new AiCleanupTool(OSKind.Linux, AiJob.Everything),
        };

    /// <summary>
    /// The tools for one OS, wired to whatever the caller supplies. The Windows set
    /// needs an <see cref="IExplorerAdvancedSettings"/>; asking for it on a machine
    /// that is not Windows without supplying one is a programming error, because the
    /// registry-backed implementation cannot exist there.
    /// </summary>
    public static IReadOnlyList<ITool> For(
        OSKind os, IProcessRunner runner, IExplorerAdvancedSettings? explorer = null) => os switch
    {
        OSKind.Windows => Windows(runner, explorer ?? CreateExplorerSettings()),
        OSKind.MacOS => MacOS(runner),
        _ => Linux(runner),
    };

    /// <summary>
    /// How many tools an OS ships. Used for the sidebar badge tooltips, which name
    /// the count for all three platforms, not just the one running.
    /// </summary>
    public static int CountFor(OSKind os)
    {
        var runner = new ProcessRunner();
        return os switch
        {
            OSKind.Windows => Windows(
                runner,
                new UnavailableExplorerSettings(),
                new UnavailableRegistry(),
                new UnavailableRecycleBin(),
                new ElevationService(runner),
                new ExplorerController(),
                new UnavailableAppearance(),
                new UnavailableUserRegistry(),
                new UnavailableTaskbar(),
                new UnavailableKeeper()).Count,
            OSKind.MacOS => MacOS(runner).Count,
            _ => Linux(runner).Count,
        };
    }

    static IRegistryAccess CreateRegistry()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows tool set needs an IRegistryAccess supplied when built off Windows.");
        return new WindowsRegistryAccess();
    }

    static IRecycleBin CreateRecycleBin()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows tool set needs an IRecycleBin supplied when built off Windows.");
        return new ShellRecycleBin();
    }

    static ITaskbarKeeperProcess CreateKeeper()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows tool set needs an ITaskbarKeeperProcess supplied when built off Windows.");
        return new WindowsTaskbarKeeperProcess();
    }

    static ITaskbarController CreateTaskbar()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows tool set needs an ITaskbarController supplied when built off Windows.");
        return new WindowsTaskbarController();
    }

    static IUserRegistry CreateUserRegistry()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows tool set needs an IUserRegistry supplied when built off Windows.");
        return new WindowsUserRegistry();
    }

    static IWindowsAppearance CreateAppearance()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows tool set needs an IWindowsAppearance supplied when built off Windows.");
        return new RegistryWindowsAppearance();
    }

    static IExplorerAdvancedSettings CreateExplorerSettings()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows tool set needs an IExplorerAdvancedSettings supplied when built off Windows.");
        return new RegistryExplorerSettings();
    }

    /// <summary>
    /// Stands in where a Windows tool has to be constructed but must never run — only
    /// <see cref="CountFor"/> does that. Every member throws rather than returning a
    /// plausible default, so a mistake here surfaces instead of quietly reporting that
    /// Explorer is hiding your files.
    /// </summary>
    sealed class UnavailableExplorerSettings : IExplorerAdvancedSettings
    {
        public int Hidden
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public int HideFileExt
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public void NotifyShell() => throw new PlatformNotSupportedException();
    }

    /// <summary>As above, for the registry-backed tools.</summary>
    sealed class UnavailableRegistry : IRegistryAccess
    {
        public object? GetValue(RegHive hive, string keyPath, string name) =>
            throw new PlatformNotSupportedException();
        public IReadOnlyList<string> ValueNames(RegHive hive, string keyPath) =>
            throw new PlatformNotSupportedException();
        public IReadOnlyList<string> SubKeyNames(RegHive hive, string keyPath) =>
            throw new PlatformNotSupportedException();
        public bool DeleteValue(RegHive hive, string keyPath, string name) =>
            throw new PlatformNotSupportedException();
        public bool DeleteSubKeyTree(RegHive hive, string keyPath, string subKey) =>
            throw new PlatformNotSupportedException();
    }

    /// <summary>As above, for writing per-user registry values.</summary>
    sealed class UnavailableUserRegistry : IUserRegistry
    {
        public int? GetDword(string keyPath, string name) => throw new PlatformNotSupportedException();
        public void SetDword(string keyPath, string name, int value) => throw new PlatformNotSupportedException();
        public bool KeyExists(string keyPath) => throw new PlatformNotSupportedException();
        public string? GetString(string keyPath, string name) => throw new PlatformNotSupportedException();
        public void SetString(string keyPath, string name, string value) => throw new PlatformNotSupportedException();
        public void DeleteValue(string keyPath, string name) => throw new PlatformNotSupportedException();
        public void CreateKeyWithEmptyDefault(string keyPath) => throw new PlatformNotSupportedException();
        public void DeleteKeyTree(string keyPath) => throw new PlatformNotSupportedException();
        public void BroadcastSettingChange(string area) => throw new PlatformNotSupportedException();
    }

    /// <summary>As above, for the taskbar.</summary>
    sealed class UnavailableTaskbar : ITaskbarController
    {
        public bool Exists => throw new PlatformNotSupportedException();
        public bool IsHidden => throw new PlatformNotSupportedException();
        public bool AutoHide
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }
        public int SetVisible(bool visible) => throw new PlatformNotSupportedException();
        public IReadOnlyList<long> Windows() => throw new PlatformNotSupportedException();
    }

    /// <summary>As above, for the taskbar keeper.</summary>
    sealed class UnavailableKeeper : ITaskbarKeeperProcess
    {
        public bool IsRunning => throw new PlatformNotSupportedException();
        public void Start(string exe) => throw new PlatformNotSupportedException();
        public void Stop() => throw new PlatformNotSupportedException();
    }

    /// <summary>As above, for the dark/light mode values.</summary>
    sealed class UnavailableAppearance : IWindowsAppearance
    {
        public int AppsUseLightTheme
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public int SystemUsesLightTheme
        {
            get => throw new PlatformNotSupportedException();
            set => throw new PlatformNotSupportedException();
        }

        public void Broadcast() => throw new PlatformNotSupportedException();
    }

    /// <summary>As above, for the Recycle Bin.</summary>
    sealed class UnavailableRecycleBin : IRecycleBin
    {
        public RecycleBinState Query() => throw new PlatformNotSupportedException();
        public bool Empty() => throw new PlatformNotSupportedException();
    }
}
