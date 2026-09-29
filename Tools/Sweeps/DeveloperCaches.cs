namespace OsXos.Tools.Sweeps;

/// <summary>
/// The download caches package managers and build tools keep, which grow without
/// limit and are re-fetched on demand. A fixed list of each tool's documented cache
/// location — never a search for <c>node_modules</c> or anything else project-shaped,
/// because a project folder is somebody's work, not a cache.
///
/// Deliberately absent: Maven's <c>~/.m2/repository</c>, which also holds artifacts a
/// developer installed locally and cannot re-download; Go's module cache, whose files
/// are read-only and belong to <c>go clean -modcache</c>; and pnpm's store, whose
/// hard links projects depend on and which has <c>pnpm store prune</c> for exactly this.
/// </summary>
public static class DeveloperCaches
{
    public static LocationJob For(OSKind os) => new(
        Slug: "developer-caches",
        Platform: os,
        // Linux has no Developer category; its package-shaped page is Packages.
        Category: os == OSKind.Linux ? ToolCategory.Packages : ToolCategory.Developer,
        Name: "Clear Developer Caches",
        Summary: "Delete the download caches npm, pip, NuGet, Cargo, Gradle, Go and friends keep — all re-fetched on demand.",
        IconKey: "IconDeveloper",
        Warning:
            "Close IDEs and stop running builds first — a build in progress holds its cache open. " +
            "Nothing here is irreplaceable: the next install or build downloads what it needs again, which costs time and bandwidth once.",
        Steps: new ToolStep[]
        {
            new("Look where each tool keeps its downloads",
                "npm, Yarn, Bun, Deno and node-gyp; pip and uv; NuGet's HTTP cache and global packages folder; Cargo's registry; Gradle's caches and wrapper downloads; and Go's build cache. Each at the location its own documentation names, and nowhere else."),
            new("Never a project folder",
                "osXos does not go looking for node_modules, target/ or bin/ folders — those live inside projects, and a project is somebody's work. Maven's local repository, Go's module cache and pnpm's store are left out on purpose too: each can hold something that is not re-downloadable, or has its own safe command."),
            new("Measure each one before anything is touched",
                "The Review stage lists every cache found on this machine with its real size. The scan only reads, and a tool that is not installed contributes nothing."),
            new("Delete the rest",
                "Files held open by a running build are skipped rather than forced, counted, and named afterwards. The next install or build re-downloads what it needs."),
        },
        Map: p => Map(os, p),
        NothingFound: "Nothing to clear — none of the package managers osXos knows about has a cache on this machine.",
        Afterwards: "The next install or build re-downloads whatever it needs.");

    public static IReadOnlyList<Location> Map(OSKind os, ProfileRoots p)
    {
        var home = p.Home;
        var cache = p.AppCache;
        var list = new List<Location>();

        void Add(string product, string label, string path) => list.Add(new Location(product, label, path));

        // npm keeps one directory under the profile everywhere but Windows.
        var npm = os == OSKind.Windows ? Path.Combine(cache, "npm-cache") : Path.Combine(home, ".npm");
        Add("npm", "package cache", Path.Combine(npm, "_cacache"));
        Add("npm", "npx packages", Path.Combine(npm, "_npx"));
        Add("npm", "logs", Path.Combine(npm, "_logs"));

        Add("Yarn", "package cache", os switch
        {
            OSKind.Windows => Path.Combine(cache, "Yarn", "Cache"),
            OSKind.MacOS => Path.Combine(cache, "Yarn"),
            _ => Path.Combine(cache, "yarn"),
        });
        Add("node-gyp", "header downloads",
            os == OSKind.Windows ? Path.Combine(cache, "node-gyp", "Cache") : Path.Combine(cache, "node-gyp"));
        Add("Bun", "package cache", Path.Combine(home, ".bun", "install", "cache"));
        Add("Deno", "module cache", Path.Combine(cache, "deno"));

        Add("pip", "wheel cache", os == OSKind.Windows ? Path.Combine(cache, "pip", "cache") : Path.Combine(cache, "pip"));
        // uv follows the XDG layout on macOS too, rather than ~/Library/Caches.
        Add("uv", "package cache", os switch
        {
            OSKind.Windows => Path.Combine(cache, "uv", "cache"),
            OSKind.MacOS => Path.Combine(home, ".cache", "uv"),
            _ => Path.Combine(cache, "uv"),
        });

        var nuget = os == OSKind.Windows ? Path.Combine(cache, "NuGet") : Path.Combine(home, ".local", "share", "NuGet");
        Add("NuGet", "HTTP cache", Path.Combine(nuget, "v3-cache"));
        if (os != OSKind.Windows) Add("NuGet", "HTTP cache (newer SDKs)", Path.Combine(nuget, "http-cache"));
        Add("NuGet", "plugin cache", Path.Combine(nuget, "plugins-cache"));
        Add("NuGet", "global packages", Path.Combine(home, ".nuget", "packages"));

        Add("Go", "build cache", Path.Combine(cache, "go-build"));

        Add("Cargo", "registry downloads", Path.Combine(home, ".cargo", "registry", "cache"));
        Add("Cargo", "registry sources", Path.Combine(home, ".cargo", "registry", "src"));
        Add("Cargo", "git checkouts", Path.Combine(home, ".cargo", "git", "checkouts"));

        Add("Gradle", "build caches", Path.Combine(home, ".gradle", "caches"));
        Add("Gradle", "wrapper downloads", Path.Combine(home, ".gradle", "wrapper", "dists"));

        return list;
    }
}
