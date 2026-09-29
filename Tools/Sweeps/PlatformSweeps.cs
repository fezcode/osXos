namespace OsXos.Tools.Sweeps;

/// <summary>
/// The sweep tools that only make sense on one platform. Each is a
/// <see cref="LocationJob"/> like the cross-platform ones: a literal list of known
/// locations, measured on Review, deleted only after you have seen it.
/// </summary>
public static class PlatformSweeps
{
    // ----------------------------------------------------------------- Windows --

    /// <summary>
    /// The compiled-shader caches DirectX and the GPU drivers keep. Rebuilt on the
    /// fly, routinely gigabytes on a gaming machine, and the standard fix for a game
    /// that stutters or glitches after a driver update.
    /// </summary>
    public static LocationJob ShaderCaches => new(
        Slug: "shader-caches",
        Platform: OSKind.Windows,
        Category: ToolCategory.Maintenance,
        Name: "Clear Shader Caches",
        Summary: "Delete the compiled shaders DirectX, NVIDIA, AMD and Intel drivers keep — rebuilt as games and apps need them.",
        IconKey: "IconImage",
        Warning:
            "Close games and 3D applications first — a running one holds its shaders open. " +
            "Games stutter briefly the first time you play them afterwards while the cache refills; that is the whole cost.",
        Steps: new ToolStep[]
        {
            new("Look in the driver shader caches",
                @"DirectX's own D3DSCache, NVIDIA's DXCache and GLCache, AMD's DxCache, DxcCache, VkCache and GLCache, and Intel's ShaderCache under AppData\LocalLow. All inside your own profile."),
            new("Measure each one",
                "The Review stage lists every cache found with its real size. A vendor whose driver is not installed contributes nothing."),
            new("Delete the rest",
                "Files a running game holds open are skipped, counted and named afterwards. Nothing here is a setting or a save game — only compiled shaders."),
            new("The driver rebuilds on demand",
                "Each shader is compiled again the first time something needs it, which shows up as brief stutter the first time a game loads a level. Clearing the cache is the usual first step after a driver update causes glitches."),
        },
        Map: p =>
        {
            var local = p.AppCache;
            var low = Path.Combine(p.Home, "AppData", "LocalLow");
            return new Location[]
            {
                new("DirectX", "shader cache", Path.Combine(local, "D3DSCache")),
                new("NVIDIA", "DirectX shader cache", Path.Combine(local, "NVIDIA", "DXCache")),
                new("NVIDIA", "OpenGL shader cache", Path.Combine(local, "NVIDIA", "GLCache")),
                new("NVIDIA", "legacy shader cache", Path.Combine(local, "NVIDIA Corporation", "NV_Cache")),
                new("AMD", "DirectX shader cache", Path.Combine(local, "AMD", "DxCache")),
                new("AMD", "DirectX 12 shader cache", Path.Combine(local, "AMD", "DxcCache")),
                new("AMD", "Vulkan shader cache", Path.Combine(local, "AMD", "VkCache")),
                new("AMD", "OpenGL shader cache", Path.Combine(local, "AMD", "GLCache")),
                new("Intel", "shader cache", Path.Combine(low, "Intel", "ShaderCache")),
            };
        },
        NothingFound: "Nothing to clear — no DirectX or GPU driver shader cache was found on this machine.",
        Afterwards: "Shaders are compiled again the first time something needs them.");

    /// <summary>
    /// The crash dumps applications write and the error reports Windows queues for
    /// them, per user. Useful to a developer debugging that exact crash, and to nobody
    /// once it is fixed.
    /// </summary>
    public static LocationJob CrashReports => new(
        Slug: "crash-reports",
        Platform: OSKind.Windows,
        Category: ToolCategory.Maintenance,
        Name: "Clear Crash Dumps & Error Reports",
        Summary: "Delete the crash dumps apps leave in your profile and the error reports Windows has queued or archived.",
        IconKey: "IconWarning",
        Warning:
            "If you are waiting on a vendor to investigate a crash, keep its dump until they have it. " +
            "Otherwise nothing here is used again: the reports have been sent or never will be.",
        Steps: new ToolStep[]
        {
            new("Look where crashes are recorded",
                @"%LOCALAPPDATA%\CrashDumps, where applications write a memory dump when they crash, and the per-user Windows Error Reporting folders under %LOCALAPPDATA%\Microsoft\Windows\WER: ReportQueue, for reports not yet sent, and ReportArchive, for ones that were."),
            new("Keep the folders, empty them",
                "Windows expects these folders to exist and writes into them next time something crashes, so only their contents go."),
            new("Measure each one",
                "Dumps are often hundreds of megabytes each. The Review stage lists every folder found with its real size, and changes nothing."),
            new("Delete",
                "Only your own account's reports. The machine-wide ones under ProgramData need administrator rights and are left alone."),
        },
        Map: p =>
        {
            var wer = Path.Combine(p.AppCache, "Microsoft", "Windows", "WER");
            return new Location[]
            {
                new("Applications", "crash dumps", Path.Combine(p.AppCache, "CrashDumps")) { ContentsOnly = true },
                new("Windows Error Reporting", "queued reports", Path.Combine(wer, "ReportQueue")) { ContentsOnly = true },
                new("Windows Error Reporting", "archived reports", Path.Combine(wer, "ReportArchive")) { ContentsOnly = true },
                new("Windows Error Reporting", "working files", Path.Combine(wer, "Temp")) { ContentsOnly = true },
            };
        },
        NothingFound: "Nothing to clear — there are no crash dumps or error reports in your profile.",
        Afterwards: "Windows writes new reports into the same folders the next time something crashes.");

    // ------------------------------------------------------------------- macOS --

    /// <summary>
    /// What Xcode builds and caches under your Library. DerivedData alone is routinely
    /// tens of gigabytes, and is rebuilt on the next build of each project.
    /// </summary>
    public static LocationJob XcodeData => new(
        Slug: "xcode-data",
        Platform: OSKind.MacOS,
        Category: ToolCategory.Developer,
        Name: "Clear Xcode Build Data",
        Summary: "Delete DerivedData, device support files and Xcode's caches — rebuilt on the next build or device connection.",
        IconKey: "IconDeveloper",
        Warning:
            "Quit Xcode first. The next build of each project starts from scratch, and the first time you connect a device its support files are copied again.",
        Steps: new ToolStep[]
        {
            new("Look where Xcode keeps build products",
                "~/Library/Developer/Xcode/DerivedData, which holds every project's intermediate build products and indexes; the iOS, watchOS, tvOS and visionOS DeviceSupport folders, copied from each device and OS version you have ever connected; the CoreSimulator cache; and Xcode's own cache under ~/Library/Caches."),
            new("Leave what cannot come back",
                "Archives are not on the list — they hold the symbols needed to read crash reports from builds you shipped. Simulator devices and their data are not either; Delete Unavailable Simulators handles the ones that are dead weight."),
            new("Measure each one",
                "The Review stage lists every folder found with its real size, and changes nothing."),
            new("Delete",
                "DerivedData is emptied rather than removed, since Xcode expects to find it. Everything here is rebuilt the next time Xcode needs it."),
        },
        Map: p =>
        {
            var xcode = Path.Combine(p.Home, "Library", "Developer", "Xcode");
            return new Location[]
            {
                new("Xcode", "DerivedData", Path.Combine(xcode, "DerivedData")) { ContentsOnly = true },
                new("Xcode", "iOS device support", Path.Combine(xcode, "iOS DeviceSupport")),
                new("Xcode", "watchOS device support", Path.Combine(xcode, "watchOS DeviceSupport")),
                new("Xcode", "tvOS device support", Path.Combine(xcode, "tvOS DeviceSupport")),
                new("Xcode", "visionOS device support", Path.Combine(xcode, "visionOS DeviceSupport")),
                new("Xcode", "simulator cache", Path.Combine(p.Home, "Library", "Developer", "CoreSimulator", "Caches")),
                new("Xcode", "application cache", Path.Combine(p.AppCache, "com.apple.dt.Xcode")),
            };
        },
        NothingFound: "Nothing to clear — Xcode has left no build data on this Mac.",
        Afterwards: "Each project rebuilds its DerivedData on the next build.");

    // ------------------------------------------------------------------- Linux --

    /// <summary>
    /// The desktop Trash in your home, as the freedesktop.org specification lays it
    /// out: the trashed files, the records of where they came from, and anything a
    /// file manager was midway through expunging.
    /// </summary>
    public static LocationJob LinuxTrash => new(
        Slug: "empty-trash",
        Platform: OSKind.Linux,
        Category: ToolCategory.Maintenance,
        Name: "Empty Trash",
        Summary: "Permanently delete what is in your desktop Trash, with the real count and size first.",
        IconKey: "IconTrash",
        Warning: "Emptied is gone. There is no second Trash behind this one.",
        Steps: new ToolStep[]
        {
            new("Look in your home Trash",
                "$XDG_DATA_HOME/Trash — usually ~/.local/share/Trash — which every freedesktop.org file manager shares: files/ holds what was trashed, info/ records where each came from, expunged/ holds anything a file manager was part-way through deleting."),
            new("Measure it",
                "The Review stage shows the size of what is in each, before anything is touched."),
            new("Empty it, and keep the folders",
                "Only the contents go; the folders themselves stay, so a file manager that is watching them keeps working."),
            new("Other drives are not included",
                "Files trashed on a USB stick or second drive go to a .Trash folder on that drive, and are emptied from the file manager. osXos only empties the Trash in your home."),
        },
        Map: p =>
        {
            var trash = Path.Combine(p.Data, "Trash");
            return new Location[]
            {
                new("Trash", "trashed files", Path.Combine(trash, "files")) { ContentsOnly = true },
                new("Trash", "where each came from", Path.Combine(trash, "info")) { ContentsOnly = true },
                new("Trash", "part-deleted files", Path.Combine(trash, "expunged")) { ContentsOnly = true },
            };
        },
        NothingFound: "The Trash is already empty.",
        Afterwards: "Your file manager shows the Trash as empty.");
}
