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
    public static IReadOnlyList<ITool> Windows(IProcessRunner runner, IExplorerAdvancedSettings explorer) =>
        new ITool[]
        {
            new IconCacheTool(),
            new TempFolderTool(),
            new HiddenFilesTool(explorer),
            new Tools.Windows.FlushDnsTool(runner),
        };

    public static IReadOnlyList<ITool> MacOS(IProcessRunner runner) =>
        new ITool[]
        {
            new Tools.MacOS.IconServicesCacheTool(runner),
            new Tools.MacOS.UserCachesTool(),
            new Tools.MacOS.FinderHiddenFilesTool(runner),
            new Tools.MacOS.FlushDnsTool(runner),
        };

    public static IReadOnlyList<ITool> Linux(IProcessRunner runner) =>
        new ITool[]
        {
            new Tools.Linux.ThumbnailCacheTool(),
            new Tools.Linux.UserCacheTool(),
            new Tools.Linux.IconCacheRebuildTool(runner),
            new Tools.Linux.FlushDnsTool(runner),
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
            OSKind.Windows => Windows(runner, new UnavailableExplorerSettings()).Count,
            OSKind.MacOS => MacOS(runner).Count,
            _ => Linux(runner).Count,
        };
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
}
