namespace OsXos.Tools.Sweeps;

/// <summary>
/// The disk caches web browsers keep for every profile — and only those. Cookies,
/// saved passwords, history, bookmarks, extensions and open tabs live in files beside
/// them and are not in this map, so no run of this tool can sign you out of anything.
///
/// Chromium browsers (Chrome, Edge, Brave, Vivaldi, Chromium) and Firefox. Safari is
/// absent: macOS protects its cache behind Full Disk Access, and a tool that could
/// only measure it by asking for access to everything is the wrong trade.
/// </summary>
public static class BrowserCaches
{
    public static LocationJob For(OSKind os) => new(
        Slug: "browser-caches",
        Platform: os,
        // Linux has no Privacy category; clearing caches is maintenance there.
        Category: os == OSKind.Linux ? ToolCategory.Maintenance : ToolCategory.Privacy,
        Name: "Clear Browser Caches",
        Summary: "Delete the page, code and GPU caches of Chrome, Edge, Brave, Vivaldi and Firefox — never cookies or logins.",
        IconKey: "IconNetwork",
        Warning:
            "Close your browsers first — a running one holds its cache open and those files are skipped. " +
            "You stay signed in everywhere: cookies, passwords, history and bookmarks are not touched. Sites load a little slower the first time afterwards.",
        Steps: new ToolStep[]
        {
            new("Find each browser's profiles",
                "Chrome, Edge, Brave, Vivaldi and Chromium keep one folder per profile — Default, Profile 1, Profile 2 — and Firefox one per profile under its Profiles folder. Every profile is covered, each listed separately."),
            new("Take only the caches",
                "Per profile: the HTTP disk cache, the compiled-JavaScript Code Cache, the GPU cache and the service-worker caches; for Firefox, cache2 and the startup cache. Plus the shared shader caches each Chromium browser keeps beside its profiles."),
            new("Leave everything that is yours",
                "Cookies, saved passwords, history, bookmarks, autofill, extensions and open tabs live in other files in the same profile folders. None of them are in the map this tool works from, so none can appear on the Review list."),
            new("Delete, and let the browser rebuild",
                "Files a running browser holds open are skipped, counted and named afterwards. Pages are fetched fresh the next time you visit them; that is the whole cost."),
        },
        Map: p => Map(os, p),
        NothingFound: "Nothing to clear — none of the browsers osXos knows about has a cache on this machine.",
        Afterwards: "You are still signed in everywhere. Pages load fresh the first time you revisit them.");

    /// <summary>A Chromium browser: where its profiles live, and where their disk caches live.</summary>
    sealed record Chromium(string Product, string DataDir, string CacheDir);

    static IEnumerable<Chromium> ChromiumBrowsers(OSKind os, ProfileRoots p) => os switch
    {
        OSKind.Windows => new[]
        {
            Same("Chrome", Path.Combine(p.AppCache, "Google", "Chrome", "User Data")),
            Same("Edge", Path.Combine(p.AppCache, "Microsoft", "Edge", "User Data")),
            Same("Brave", Path.Combine(p.AppCache, "BraveSoftware", "Brave-Browser", "User Data")),
            Same("Vivaldi", Path.Combine(p.AppCache, "Vivaldi", "User Data")),
            Same("Chromium", Path.Combine(p.AppCache, "Chromium", "User Data")),
        },
        OSKind.MacOS => new[]
        {
            Split("Chrome", p, "Google", "Chrome"),
            Split("Edge", p, "Microsoft Edge"),
            Split("Brave", p, "BraveSoftware", "Brave-Browser"),
            Split("Vivaldi", p, "Vivaldi"),
            Split("Chromium", p, "Chromium"),
        },
        _ => new[]
        {
            Split("Chrome", p, "google-chrome"),
            Split("Edge", p, "microsoft-edge"),
            Split("Brave", p, "BraveSoftware", "Brave-Browser"),
            Split("Vivaldi", p, "vivaldi"),
            Split("Chromium", p, "chromium"),
        },
    };

    // Windows keeps the disk cache inside the profile; macOS and Linux move it to the
    // platform's cache root under the same relative path.
    static Chromium Same(string product, string dir) => new(product, dir, dir);

    static Chromium Split(string product, ProfileRoots p, params string[] relative) =>
        new(product, Path.Combine(new[] { p.AppSupport }.Concat(relative).ToArray()),
            Path.Combine(new[] { p.AppCache }.Concat(relative).ToArray()));

    static string FirefoxCacheRoot(OSKind os, ProfileRoots p) => os switch
    {
        OSKind.Windows => Path.Combine(p.AppCache, "Mozilla", "Firefox", "Profiles"),
        OSKind.MacOS => Path.Combine(p.AppCache, "Firefox", "Profiles"),
        _ => Path.Combine(p.AppCache, "mozilla", "firefox"),
    };

    static readonly string[] ProfileCaches = { "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache" };
    static readonly string[] SharedCaches = { "GrShaderCache", "ShaderCache", "GraphiteDawnCache" };

    public static IReadOnlyList<Location> Map(OSKind os, ProfileRoots p)
    {
        var list = new List<Location>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string product, string label, string path)
        {
            // Windows' data and cache directories coincide, so the same folder can be
            // named twice; the second would be counted and deleted twice.
            if (seen.Add(Path.GetFullPath(path))) list.Add(new Location(product, label, path));
        }

        foreach (var b in ChromiumBrowsers(os, p))
        {
            foreach (var profile in ChromiumProfiles(b))
            {
                foreach (var root in new[] { b.CacheDir, b.DataDir })
                    foreach (var name in ProfileCaches)
                        Add(b.Product, $"{profile} — {Describe(name)}", Path.Combine(root, profile, name));

                var sw = Path.Combine(b.DataDir, profile, "Service Worker");
                Add(b.Product, $"{profile} — service-worker cache", Path.Combine(sw, "CacheStorage"));
                Add(b.Product, $"{profile} — service-worker scripts", Path.Combine(sw, "ScriptCache"));
            }

            foreach (var name in SharedCaches)
                Add(b.Product, "shared " + Describe(name), Path.Combine(b.DataDir, name));
        }

        foreach (var profile in Subdirectories(FirefoxCacheRoot(os, p)))
        {
            var name = Path.GetFileName(profile);
            Add("Firefox", $"{name} — disk cache", Path.Combine(profile, "cache2"));
            Add("Firefox", $"{name} — startup cache", Path.Combine(profile, "startupCache"));
        }

        return list;
    }

    static string Describe(string folder) => folder switch
    {
        "Cache" => "disk cache",
        "Code Cache" => "compiled script cache",
        "GPUCache" => "GPU cache",
        "GrShaderCache" or "ShaderCache" or "GraphiteDawnCache" or "DawnGraphiteCache" or "DawnWebGPUCache" => "shader cache",
        _ => folder,
    };

    /// <summary>
    /// Default and Profile N, from either directory. Guest and System profiles are
    /// Chromium's own scratch and are covered by nothing here.
    /// </summary>
    static IEnumerable<string> ChromiumProfiles(Chromium b) =>
        Subdirectories(b.DataDir).Concat(Subdirectories(b.CacheDir))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => n == "Default" || n.StartsWith("Profile ", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n == "Default" ? "" : n, StringComparer.Ordinal);

    static IEnumerable<string> Subdirectories(string dir)
    {
        try { return Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
