using OsXos.Tools.Linux;

namespace OsXos.Tools;

/// <summary>
/// One known place something leaves files behind. <paramref name="Path"/> is absolute
/// and always resolved from a <see cref="ProfileRoots"/>, never from the environment
/// directly, so a whole map can be pointed at a temp directory in a test.
///
/// Every sweep tool in osXos works from a literal list of these rather than a search:
/// a tool that went looking for anything cache-shaped under your profile would
/// eventually find something it should not have, and the Review stage would be the
/// only thing standing between that guess and your files.
/// </summary>
public record Location(string Product, string Label, string Path)
{
    /// <summary>
    /// Set when this location is a container rather than a thing: its immediate
    /// subdirectories become the rows, and inside each of those a child of this name
    /// is left alone. <c>~/.claude/projects</c>, with a <c>memory/</c> folder beside
    /// the transcripts, is the shape this exists for.
    /// </summary>
    public string? PreservedChild { get; init; }

    /// <summary>
    /// Set when the last segment of <see cref="Path"/> is a wildcard rather than a
    /// name: the location is every entry directly inside that directory whose name
    /// matches it, shown as one row. Only ever with a literal product prefix in front
    /// of the wildcard — <c>codex-clipboard-*.png</c>, never <c>*.png</c>.
    /// </summary>
    public bool IsPattern { get; init; }

    /// <summary>
    /// Set when the folder itself must survive and only what is inside it goes — a
    /// Trash directory the desktop expects to find, a cache root another program
    /// holds a handle on. One row, like a plain location; the sweep empties it.
    /// </summary>
    public bool ContentsOnly { get; init; }
}

/// <summary>
/// The roots every sweep path is built from, as one value. Supplying them rather than
/// reading the environment is what lets each map be a pure function — tests build a
/// fake home under a temp directory, and a tool's Run rebuilds the same map its
/// Inspect measured instead of carrying a mutable list between the two stages.
/// </summary>
public sealed record ProfileRoots(string Home, string AppSupport, string AppCache, string Temp)
{
    string? _data;

    /// <summary>
    /// Where applications keep data that is neither configuration nor cache — the XDG
    /// data home on Linux (<c>~/.local/share</c>), Application Support on macOS, and
    /// Local AppData on Windows.
    /// </summary>
    public string Data
    {
        get => _data ?? System.IO.Path.Combine(Home, ".local", "share");
        init => _data = value;
    }

    /// <summary>
    /// Where this OS actually keeps them. <c>AppSupport</c> is the roaming,
    /// configuration-shaped root and <c>AppCache</c> the local, throwaway-shaped one;
    /// Windows splits them as Roaming and Local, macOS as Application Support and
    /// Caches, Linux as the XDG config and cache homes.
    /// </summary>
    public static ProfileRoots Current(OSKind os)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return os switch
        {
            OSKind.Windows => new ProfileRoots(
                home,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                System.IO.Path.GetTempPath())
            {
                Data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            },

            OSKind.MacOS => new ProfileRoots(
                home,
                System.IO.Path.Combine(home, "Library", "Application Support"),
                System.IO.Path.Combine(home, "Library", "Caches"),
                Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } tmp ? tmp : "/tmp")
            {
                Data = System.IO.Path.Combine(home, "Library", "Application Support"),
            },

            _ => new ProfileRoots(
                XdgPaths.Home,
                XdgPaths.ConfigHome,
                XdgPaths.CacheHome,
                "/tmp")
            {
                Data = XdgPaths.DataHome,
            },
        };
    }
}

/// <summary>
/// The two operations over a map of <see cref="Location"/>s: turning one into the
/// rows the Review stage shows, and deleting rows the user has approved.
/// </summary>
public static class LocationSweep
{
    /// <summary>
    /// What one location actually amounts to on this machine, as Review rows. A
    /// location that is not there yields nothing — a machine that never installed a
    /// product is not an error, it is the normal case.
    /// </summary>
    public static IEnumerable<PreviewItem> Rows(Location location, CancellationToken ct = default)
    {
        var label = $"{location.Product} · {location.Label}";

        if (location.IsPattern)
        {
            long total = 0;
            var any = false;
            foreach (var match in Matches(location.Path))
            {
                ct.ThrowIfCancellationRequested();
                any = true;
                total += Measure(match, ct) ?? 0;
            }

            if (!any) yield break;
            yield return new PreviewItem(label, location.Path, total);
            yield break;
        }

        if (location.ContentsOnly)
        {
            var children = Children(location.Path).ToList();
            if (children.Count == 0) yield break;
            yield return new PreviewItem(label, location.Path, children.Sum(c => Measure(c, ct) ?? 0));
            yield break;
        }

        if (location.PreservedChild is null)
        {
            var size = Measure(location.Path, ct);
            if (size is null) yield break;
            yield return new PreviewItem(label, location.Path, size);
            yield break;
        }

        string[] subdirs;
        try { subdirs = Directory.GetDirectories(location.Path); }
        catch { yield break; }

        foreach (var sub in subdirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            long total = 0;
            var any = false;
            foreach (var child in Children(sub, except: location.PreservedChild))
            {
                any = true;
                total += Measure(child, ct) ?? 0;
            }

            if (!any) continue;
            yield return new PreviewItem($"{label} — {System.IO.Path.GetFileName(sub)}", sub, total);
        }
    }

    /// <summary>Every row a whole map produces, in map order.</summary>
    public static List<PreviewItem> Rows(IEnumerable<Location> map, CancellationToken ct = default)
    {
        var items = new List<PreviewItem>();
        foreach (var location in map)
        {
            ct.ThrowIfCancellationRequested();
            items.AddRange(Rows(location, ct));
        }
        return items;
    }

    /// <summary>
    /// Deletes rows the user approved on Review. A row that stands for several things
    /// — a pattern's matches, a folder to empty, a project with a folder to keep — is
    /// expanded into exactly those things first, looked up in the map rather than
    /// guessed from the path, so a preserved folder is never handed to the sweep at
    /// all rather than being handed over and skipped.
    /// </summary>
    public static SweepOutcome Delete(
        IReadOnlyList<Location> map,
        IEnumerable<PreviewItem> items,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default)
    {
        var expanded = new List<PreviewItem>();

        foreach (var item in items)
        {
            var path = item.Detail;
            if (string.IsNullOrEmpty(path)) continue;

            var own = map.FirstOrDefault(l => Same(l.Path, path));
            if (own is { IsPattern: true })
            {
                foreach (var match in Matches(path))
                    expanded.Add(new PreviewItem(item.Label, match, Measure(match, ct)));
                continue;
            }

            if (own is { ContentsOnly: true })
            {
                foreach (var child in Children(path))
                    expanded.Add(new PreviewItem(item.Label, child, Measure(child, ct)));
                continue;
            }

            var parent = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path));
            var container = parent is null
                ? null
                : map.FirstOrDefault(l => l.PreservedChild is not null && Same(l.Path, parent));
            if (container is not null)
            {
                foreach (var child in Children(path, except: container.PreservedChild))
                    expanded.Add(new PreviewItem(item.Label, child, Measure(child, ct)));
                continue;
            }

            expanded.Add(item);
        }

        return FileSweep.Delete(expanded, progress, ct);
    }

    static bool Same(string a, string b) => string.Equals(
        System.IO.Path.TrimEndingDirectorySeparator(a),
        System.IO.Path.TrimEndingDirectorySeparator(b),
        StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The entries directly inside a pattern's directory whose names match it. Simple
    /// matching, so Windows' legacy rules cannot widen <c>*.png</c> to <c>*.pngx</c>
    /// or match against an 8.3 short name.
    /// </summary>
    static IEnumerable<string> Matches(string pattern)
    {
        var dir = System.IO.Path.GetDirectoryName(pattern);
        var name = System.IO.Path.GetFileName(pattern);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) yield break;

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(dir, name, new EnumerationOptions
            {
                MatchType = MatchType.Simple,
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
            });
        }
        catch { yield break; }

        foreach (var entry in entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            yield return entry;
    }

    /// <summary>Everything directly inside <paramref name="dir"/>, optionally less one name.</summary>
    static IEnumerable<string> Children(string dir, string? except = null)
    {
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(dir); }
        catch { yield break; }

        foreach (var entry in entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            if (except is not null &&
                string.Equals(System.IO.Path.GetFileName(entry), except, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return entry;
        }
    }

    /// <summary>
    /// The size of a file or a directory tree, or null when the path is not there at
    /// all. A file is measured with one stat rather than a walk.
    /// </summary>
    static long? Measure(string path, CancellationToken ct)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }

        return Directory.Exists(path) ? FileSweep.SizeOf(path, ct) : null;
    }
}
