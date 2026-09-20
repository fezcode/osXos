namespace OsXos.Tools.Windows;

/// <summary>
/// Reports the handful of Windows settings that decide whether a development
/// toolchain will behave, and changes none of them.
///
/// It is deliberately read-only. Two of the three live in HKEY_LOCAL_MACHINE and
/// flipping them needs administrator rights; more to the point, they are settings a
/// developer should turn on knowingly rather than have a maintenance tool toggle. So
/// this reports, and says where to go.
/// </summary>
public sealed class DeveloperStatusTool : ITool
{
    public const string FileSystemKey = @"SYSTEM\CurrentControlSet\Control\FileSystem";
    public const string AppModelUnlockKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";

    readonly IRegistryAccess _registry;

    public DeveloperStatusTool(IRegistryAccess registry) => _registry = registry;

    public string Id => "windows.developer-status";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Developer;
    public string Name => "Developer Settings Report";
    public string Summary => "Check long path support, Developer Mode and script execution — read-only.";
    public string IconKey => "IconDeveloper";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read three settings",
            @"LongPathsEnabled under HKLM\SYSTEM\CurrentControlSet\Control\FileSystem, AllowDevelopmentWithoutDevLicense under HKLM\...\AppModelUnlock, and whether this build is 64-bit. Reading HKEY_LOCAL_MACHINE needs no special rights; only writing does."),
        new("Why long paths matter",
            @"Without LongPathsEnabled, Windows caps a path at 260 characters. Deep node_modules trees, nested Go module caches and long branch names hit that limit and produce errors that look like permission problems rather than length problems."),
        new("Why Developer Mode matters",
            "It permits sideloading unsigned packages and creating symbolic links without administrator rights — which is what lets package managers and build tools make symlinks the way they do on macOS and Linux."),
        new("Nothing is changed",
            "This tool only reads. Both settings live in HKEY_LOCAL_MACHINE and turning them on needs administrator rights and a restart, so they belong in Settings → System → For developers, or in Group Policy, where the change is deliberate. The Result stage tells you exactly where to go."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var longPaths = AsInt(_registry.GetValue(RegHive.LocalMachine, FileSystemKey, "LongPathsEnabled"));
        var devMode = AsInt(_registry.GetValue(
            RegHive.LocalMachine, AppModelUnlockKey, "AllowDevelopmentWithoutDevLicense"));

        var items = new List<PreviewItem>
        {
            new("Long path support (260-character limit lifted)",
                Describe(longPaths, on: "enabled", off: "disabled — paths are capped at 260 characters")),
            new("Developer Mode (sideloading, symlinks without admin)",
                Describe(devMode, on: "enabled", off: "disabled")),
            new("Process architecture",
                $"{(Environment.Is64BitProcess ? "64-bit" : "32-bit")} process on a {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} OS"),
            new(".NET runtime", Environment.Version.ToString()),
        };

        return Task.FromResult(new ToolPreview(items,
            "Read-only. Running this changes nothing on the machine."));
    }

    static int? AsInt(object? value) => value is int i ? i : null;

    static string Describe(int? value, string on, string off) => value switch
    {
        null => "not set — " + off,
        0 => off,
        _ => on,
    };

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        // "Running" a report is just repeating what Review already showed, which is
        // the honest behaviour for a tool that reads: no surprise second action.
        var lines = preview.Items
            .Select(i => $"{i.Label}: {i.Detail}")
            .Concat(new[]
            {
                "",
                "To change either of the first two: Settings → System → For developers.",
                "Long paths can also be set by Group Policy under Computer Configuration → Administrative Templates → System → Filesystem. Both need administrator rights and take effect after a restart.",
            })
            .ToArray();

        return Task.FromResult(ToolResult.Success("Developer settings read", lines));
    }
}
