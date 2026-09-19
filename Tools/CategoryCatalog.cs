namespace OsXos.Tools;

/// <summary>A category as one OS names it.</summary>
public sealed record CategoryInfo(ToolCategory Category, string Name, string IconKey);

/// <summary>
/// The per-OS category taxonomy: which categories an OS has, in sidebar order, and
/// what that OS calls them. This is the full spine — a category only reaches the
/// sidebar once a tool claims it (see <see cref="ToolRegistry.CategoriesFor"/>), so
/// listing one here that has no tools yet costs nothing and reserves the name.
/// </summary>
public static class CategoryCatalog
{
    static readonly IReadOnlyDictionary<OSKind, IReadOnlyList<CategoryInfo>> ByOs =
        new Dictionary<OSKind, IReadOnlyList<CategoryInfo>>
        {
            [OSKind.Windows] = new CategoryInfo[]
            {
                new(ToolCategory.Maintenance, "Maintenance", "IconMaintenance"),
                new(ToolCategory.Shell, "Explorer & Shell", "IconShell"),
                new(ToolCategory.System, "System", "IconSystem"),
                new(ToolCategory.Network, "Network", "IconNetwork"),
                new(ToolCategory.Privacy, "Privacy", "IconPrivacy"),
                new(ToolCategory.Developer, "Developer", "IconDeveloper"),
            },
            [OSKind.MacOS] = new CategoryInfo[]
            {
                new(ToolCategory.Maintenance, "Maintenance", "IconMaintenance"),
                new(ToolCategory.Shell, "Finder & Dock", "IconShell"),
                new(ToolCategory.System, "System", "IconSystem"),
                new(ToolCategory.Network, "Network", "IconNetwork"),
                new(ToolCategory.Privacy, "Privacy", "IconPrivacy"),
                new(ToolCategory.Developer, "Developer", "IconDeveloper"),
            },
            [OSKind.Linux] = new CategoryInfo[]
            {
                new(ToolCategory.Maintenance, "Maintenance", "IconMaintenance"),
                new(ToolCategory.Shell, "Desktop & Shell", "IconShell"),
                new(ToolCategory.System, "System", "IconSystem"),
                new(ToolCategory.Network, "Network", "IconNetwork"),
                new(ToolCategory.Packages, "Packages", "IconPackages"),
                new(ToolCategory.Services, "Services", "IconServices"),
            },
        };

    /// <summary>Every category this OS defines, in sidebar order.</summary>
    public static IReadOnlyList<CategoryInfo> For(OSKind os) => ByOs[os];

    /// <summary>
    /// How this OS names one category, or null when the category is not part of its
    /// taxonomy — a tool claiming one of those is a bug, and the registry tests say so.
    /// </summary>
    public static CategoryInfo? Find(OSKind os, ToolCategory category) =>
        ByOs[os].FirstOrDefault(c => c.Category == category);

    /// <summary>The OS this build is running on.</summary>
    public static OSKind CurrentOS =>
        OperatingSystem.IsWindows() ? OSKind.Windows :
        OperatingSystem.IsMacOS() ? OSKind.MacOS :
        OSKind.Linux;

    public static string DisplayName(OSKind os) => os switch
    {
        OSKind.Windows => "Windows",
        OSKind.MacOS => "macOS",
        _ => "Linux",
    };

    public static string IconKey(OSKind os) => os switch
    {
        OSKind.Windows => "OsWindows",
        OSKind.MacOS => "OsMacOS",
        _ => "OsLinux",
    };

    public static string BrushKey(OSKind os) => os switch
    {
        OSKind.Windows => "WindowsBrush",
        OSKind.MacOS => "MacOSBrush",
        _ => "LinuxBrush",
    };
}
