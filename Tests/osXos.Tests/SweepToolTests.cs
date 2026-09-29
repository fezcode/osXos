using OsXos.Tools;
using OsXos.Tools.Ai;
using OsXos.Tools.Sweeps;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The location-sweep tools — developer caches, browser caches, shader caches, crash
/// reports, Xcode data and the Linux Trash — and the shared sweep they run on. Every
/// test builds a fake profile under a temp directory; none touches a real cache.
/// </summary>
public class SweepToolTests
{
    static ProfileRoots Fake(TempDir dir) => new(
        dir.Sub("home"), dir.Sub("support"), dir.Sub("cache"), dir.Sub("temp")) { Data = dir.Sub("data") };

    static void Write(string path, int bytes = 16)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    static async Task<ToolResult> InspectAndRun(ITool tool) =>
        await tool.RunAsync(await tool.InspectAsync(default), default);

    public static TheoryData<OSKind> AllPlatforms => new() { OSKind.Windows, OSKind.MacOS, OSKind.Linux };

    /// <summary>Every sweep job osXos ships, on the platform it ships for.</summary>
    static IEnumerable<LocationJob> Jobs(OSKind os)
    {
        yield return DeveloperCaches.For(os);
        yield return BrowserCaches.For(os);
        if (os == OSKind.Windows)
        {
            yield return PlatformSweeps.ShaderCaches;
            yield return PlatformSweeps.CrashReports;
        }
        if (os == OSKind.MacOS) yield return PlatformSweeps.XcodeData;
        if (os == OSKind.Linux) yield return PlatformSweeps.LinuxTrash;
    }

    // ------------------------------------------------------------ every map --

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void No_map_repeats_a_path_or_nests_one_inside_another(OSKind os)
    {
        // Overlap would count bytes twice on Review and hand the sweep the same path
        // twice, the second time as a failure.
        using var dir = new TempDir();
        var roots = Fake(dir);
        PopulateBrowsers(os, roots);

        foreach (var job in Jobs(os))
        {
            var paths = new LocationCleanupTool(job, roots).Map().Select(l => l.Path).ToList();
            Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var outer in paths)
                Assert.DoesNotContain(paths, inner =>
                    inner.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Every_map_stays_inside_the_profile(OSKind os)
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        PopulateBrowsers(os, roots);

        foreach (var job in Jobs(os))
            Assert.All(new LocationCleanupTool(job, roots).Map(), l =>
                Assert.StartsWith(dir.Path + Path.DirectorySeparatorChar, l.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public async Task An_empty_profile_blocks_every_sweep_with_a_reason(OSKind os)
    {
        using var dir = new TempDir();
        foreach (var job in Jobs(os))
        {
            var preview = await new LocationCleanupTool(job, Fake(dir)).InspectAsync(default);
            Assert.False(preview.CanRun, $"{job.Slug} found something in an empty profile");
            Assert.False(string.IsNullOrWhiteSpace(preview.Blocker));
        }
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Every_sweep_is_destructive_warns_and_explains(OSKind os)
    {
        foreach (var job in Jobs(os))
        {
            var tool = new LocationCleanupTool(job, new ProfileRoots("h", "s", "c", "t"));
            Assert.True(tool.IsDestructive);
            Assert.False(string.IsNullOrWhiteSpace(tool.Warning));
            Assert.True(tool.Steps.Count >= 3);
            Assert.False(((ITool)tool).RequiresElevation);
            Assert.NotNull(CategoryCatalog.Find(os, tool.Category));
        }
    }

    // --------------------------------------------------------- contents-only --

    [Fact]
    public async Task A_contents_only_location_keeps_its_folder()
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        var trash = Path.Combine(roots.Data, "Trash");
        Write(Path.Combine(trash, "files", "old.txt"), 100);
        Write(Path.Combine(trash, "files", "folder", "nested.txt"), 50);
        Write(Path.Combine(trash, "info", "old.txt.trashinfo"), 10);

        var tool = new LocationCleanupTool(PlatformSweeps.LinuxTrash, roots);
        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.Equal(160, preview.TotalBytes);
        Assert.True(result.Ok);
        Assert.True(Directory.Exists(Path.Combine(trash, "files")));
        Assert.True(Directory.Exists(Path.Combine(trash, "info")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(trash, "files")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(trash, "info")));
    }

    [Fact]
    public async Task An_empty_contents_only_folder_is_not_offered()
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        Directory.CreateDirectory(Path.Combine(roots.Data, "Trash", "files"));

        var preview = await new LocationCleanupTool(PlatformSweeps.LinuxTrash, roots).InspectAsync(default);

        Assert.False(preview.CanRun);
    }

    // -------------------------------------------------------- developer caches --

    [Fact]
    public async Task Developer_caches_take_the_caches_and_leave_config_and_projects()
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        var home = roots.Home;

        Write(Path.Combine(home, ".npm", "_cacache", "index"), 1000);
        Write(Path.Combine(home, ".cargo", "registry", "cache", "crate.crate"), 2000);
        Write(Path.Combine(home, ".gradle", "caches", "x.bin"), 3000);
        Write(Path.Combine(roots.AppCache, "pip", "wheel.whl"), 4000);

        string[] survivors =
        {
            Path.Combine(home, ".npmrc"),
            Path.Combine(home, ".cargo", "config.toml"),
            Path.Combine(home, ".cargo", "bin", "cargo"),
            Path.Combine(home, ".gradle", "gradle.properties"),
            Path.Combine(home, ".m2", "repository", "mine", "local.jar"),
            Path.Combine(home, "go", "pkg", "mod", "cache", "x.zip"),
            Path.Combine(home, "work", "app", "node_modules", "left-pad", "index.js"),
        };
        foreach (var s in survivors) Write(s);

        var result = await InspectAndRun(new LocationCleanupTool(DeveloperCaches.For(OSKind.Linux), roots));

        Assert.True(result.Ok);
        Assert.False(Directory.Exists(Path.Combine(home, ".npm", "_cacache")));
        Assert.False(Directory.Exists(Path.Combine(home, ".cargo", "registry", "cache")));
        Assert.False(Directory.Exists(Path.Combine(home, ".gradle", "caches")));
        Assert.False(Directory.Exists(Path.Combine(roots.AppCache, "pip")));
        Assert.All(survivors, s => Assert.True(File.Exists(s), $"{s} was deleted"));
    }

    [Theory]
    [InlineData(OSKind.Windows, ToolCategory.Developer)]
    [InlineData(OSKind.MacOS, ToolCategory.Developer)]
    [InlineData(OSKind.Linux, ToolCategory.Packages)]
    public void Developer_caches_sit_where_each_platform_has_room_for_them(OSKind os, ToolCategory expected)
    {
        Assert.Equal(expected, DeveloperCaches.For(os).Category);
    }

    // ---------------------------------------------------------- browser caches --

    static void PopulateBrowsers(OSKind os, ProfileRoots p)
    {
        var chrome = os switch
        {
            OSKind.Windows => (Data: Path.Combine(p.AppCache, "Google", "Chrome", "User Data"),
                               Cache: Path.Combine(p.AppCache, "Google", "Chrome", "User Data")),
            OSKind.MacOS => (Data: Path.Combine(p.AppSupport, "Google", "Chrome"),
                             Cache: Path.Combine(p.AppCache, "Google", "Chrome")),
            _ => (Data: Path.Combine(p.AppSupport, "google-chrome"),
                  Cache: Path.Combine(p.AppCache, "google-chrome")),
        };

        foreach (var profile in new[] { "Default", "Profile 1", "Guest Profile" })
        {
            Write(Path.Combine(chrome.Cache, profile, "Cache", "Cache_Data", "f_000001"), 500);
            Write(Path.Combine(chrome.Data, profile, "Code Cache", "js", "index"), 300);
            Write(Path.Combine(chrome.Data, profile, "Cookies"), 64);
            Write(Path.Combine(chrome.Data, profile, "Login Data"), 64);
            Write(Path.Combine(chrome.Data, profile, "History"), 64);
            Write(Path.Combine(chrome.Data, profile, "Bookmarks"), 64);
        }
        Write(Path.Combine(chrome.Data, "GrShaderCache", "data_0"), 100);
        Write(Path.Combine(chrome.Data, "Local State"), 64);

        var firefoxCache = os switch
        {
            OSKind.Windows => Path.Combine(p.AppCache, "Mozilla", "Firefox", "Profiles", "abcd.default"),
            OSKind.MacOS => Path.Combine(p.AppCache, "Firefox", "Profiles", "abcd.default"),
            _ => Path.Combine(p.AppCache, "mozilla", "firefox", "abcd.default"),
        };
        Write(Path.Combine(firefoxCache, "cache2", "entries", "A1"), 700);
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public async Task Browser_caches_go_and_cookies_logins_history_and_bookmarks_stay(OSKind os)
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        PopulateBrowsers(os, roots);

        var before = Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is "Cookies" or "Login Data" or "History" or "Bookmarks" or "Local State")
            .ToList();
        Assert.NotEmpty(before);

        var tool = new LocationCleanupTool(BrowserCaches.For(os), roots);
        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.True(result.Ok);
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Chrome · Default", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Chrome · Profile 1", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Firefox · ", StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Items, i => i.Label.Contains("Guest", StringComparison.Ordinal));

        Assert.All(before, f => Assert.True(File.Exists(f), $"{f} was deleted"));
        Assert.Empty(Directory.GetFiles(dir.Path, "f_000001", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Guest Profile", StringComparison.Ordinal)));
        Assert.Empty(Directory.GetFiles(dir.Path, "A1", SearchOption.AllDirectories));
    }

    [Fact]
    public void Browser_caches_do_not_name_the_same_folder_twice_where_data_and_cache_coincide()
    {
        // On Windows a Chromium profile's data and cache directories are the same
        // folder, so the map sees each cache from both sides.
        using var dir = new TempDir();
        var roots = Fake(dir);
        PopulateBrowsers(OSKind.Windows, roots);

        var paths = BrowserCaches.Map(OSKind.Windows, roots).Select(l => Path.GetFullPath(l.Path)).ToList();

        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---------------------------------------------------------- the others --

    [Fact]
    public async Task Xcode_empties_derived_data_and_leaves_archives()
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        var xcode = Path.Combine(roots.Home, "Library", "Developer", "Xcode");
        Write(Path.Combine(xcode, "DerivedData", "App-abc", "Build", "x.o"), 5000);
        Write(Path.Combine(xcode, "iOS DeviceSupport", "17.0", "Symbols", "dyld"), 9000);
        var archive = Path.Combine(xcode, "Archives", "2026-01-01", "App.xcarchive", "Info.plist");
        Write(archive);

        var result = await InspectAndRun(new LocationCleanupTool(PlatformSweeps.XcodeData, roots));

        Assert.True(result.Ok);
        Assert.True(Directory.Exists(Path.Combine(xcode, "DerivedData")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(xcode, "DerivedData")));
        Assert.False(Directory.Exists(Path.Combine(xcode, "iOS DeviceSupport")));
        Assert.True(File.Exists(archive));
    }

    [Fact]
    public async Task Crash_reports_keep_the_folders_windows_writes_into()
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        Write(Path.Combine(roots.AppCache, "CrashDumps", "app.exe.1234.dmp"), 8000);
        Write(Path.Combine(roots.AppCache, "Microsoft", "Windows", "WER", "ReportArchive", "AppCrash_x", "Report.wer"), 200);

        var result = await InspectAndRun(new LocationCleanupTool(PlatformSweeps.CrashReports, roots));

        Assert.True(result.Ok);
        Assert.True(Directory.Exists(Path.Combine(roots.AppCache, "CrashDumps")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(roots.AppCache, "CrashDumps")));
    }

    [Fact]
    public async Task Shader_caches_include_intels_under_local_low()
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        Write(Path.Combine(roots.AppCache, "D3DSCache", "x", "y.idx"), 100);
        Write(Path.Combine(roots.Home, "AppData", "LocalLow", "Intel", "ShaderCache", "z.bin"), 200);

        var preview = await new LocationCleanupTool(PlatformSweeps.ShaderCaches, roots).InspectAsync(default);

        Assert.Equal(300, preview.TotalBytes);
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Intel · ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_result_counts_items_across_locations_honestly()
    {
        using var dir = new TempDir();
        var roots = Fake(dir);
        var files = Path.Combine(roots.Data, "Trash", "files");
        for (var i = 0; i < 5; i++) Write(Path.Combine(files, $"f{i}.txt"));

        var result = await InspectAndRun(new LocationCleanupTool(PlatformSweeps.LinuxTrash, roots));

        Assert.Contains("Removed 5 items across 1 location", result.Lines[0], StringComparison.Ordinal);
        Assert.Contains("from 1 location", result.Headline, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ Gemini fix --

    [Fact]
    public async Task Clearing_ai_caches_no_longer_takes_gemini_chats()
    {
        // ~/.gemini/tmp holds each project's saved chats despite its name; it used to
        // be classed as scratch, which made a cache clear delete conversation history.
        using var dir = new TempDir();
        var roots = Fake(dir);
        var chat = Path.Combine(roots.Home, ".gemini", "tmp", "my-project", "chats", "session-1.jsonl");
        Write(chat, 500);
        Write(Path.Combine(roots.Home, ".claude", "cache", "a.bin"), 100);

        await InspectAndRun(new AiCleanupTool(OSKind.Windows, AiJob.Caches, roots));
        Assert.True(File.Exists(chat));

        await InspectAndRun(new AiCleanupTool(OSKind.Windows, AiJob.History, roots));
        Assert.False(File.Exists(chat));
    }
}
