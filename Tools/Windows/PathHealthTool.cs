namespace OsXos.Tools.Windows;

/// <summary>
/// Reports the state of PATH: entries pointing at folders that no longer exist,
/// entries listed twice, and empty entries left by a bad edit.
///
/// Read-only, and deliberately so. A broken PATH is usually the fault of an
/// uninstaller, and the fix belongs in the environment variable editor where the
/// user can see the whole list — not in a maintenance tool quietly rewriting the
/// one variable every build tool on the machine depends on.
/// </summary>
public sealed class PathHealthTool : ITool
{
    readonly Func<string?> _readPath;

    public PathHealthTool() : this(() => Environment.GetEnvironmentVariable("PATH")) { }

    public PathHealthTool(Func<string?> readPath) => _readPath = readPath;

    public string Id => "windows.path-health";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Developer;
    public string Name => "PATH Health Check";
    public string Summary => "Find dead, duplicated and empty entries in your PATH — read-only.";
    public string IconKey => "IconTerminal";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Split PATH into its entries",
            "PATH is one string of directories separated by semicolons. This is the list every command you type is searched through, in order, and the list your build tools inherit."),
        new("Check each one",
            "A dead entry points at a folder that no longer exists, normally left behind by an uninstaller. A duplicate is harmless but makes the list harder to read. An empty entry — two semicolons together — is read by Windows as the current directory, which is a genuine hazard rather than just untidy."),
        new("Nothing is changed",
            "PATH is the single variable every build tool on the machine depends on, and a bad rewrite breaks all of them at once. osXos reports and stops."),
        new("Fixing it",
            "Press Win+R, run SystemPropertiesAdvanced, then Environment Variables. Editing the user PATH needs no administrator rights; the system PATH does. The Result stage lists the exact entries to remove."),
    };

    /// <summary>One entry and what is wrong with it, if anything.</summary>
    public sealed record Entry(string Value, bool Missing, bool Duplicate, bool Empty)
    {
        public bool IsHealthy => !Missing && !Duplicate && !Empty;

        public string Describe() =>
            Empty ? "empty entry — Windows reads this as the current directory"
            : Missing ? "folder does not exist"
            : Duplicate ? "listed more than once"
            : "ok";
    }

    /// <summary>
    /// The analysis, kept pure so it can be tested against a made-up PATH rather
    /// than whatever this machine happens to have.
    /// </summary>
    public static IReadOnlyList<Entry> Analyse(string? path, Func<string, bool> exists)
    {
        if (string.IsNullOrEmpty(path)) return Array.Empty<Entry>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<Entry>();

        foreach (var raw in path.Split(Path.PathSeparator))
        {
            // Windows tolerates quoted entries and stray whitespace; both are still
            // the same directory, so normalise before deciding anything about them.
            var value = raw.Trim().Trim('"');

            if (value.Length == 0)
            {
                entries.Add(new Entry(raw, Missing: false, Duplicate: false, Empty: true));
                continue;
            }

            var duplicate = !seen.Add(value.TrimEnd('\\', '/'));
            entries.Add(new Entry(value, Missing: !exists(value), duplicate, Empty: false));
        }

        return entries;
    }

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var entries = Analyse(_readPath(), Directory.Exists);

        if (entries.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                "PATH is empty or could not be read, which is unusual enough to be worth looking at by hand."));
        }

        var items = entries
            .Select(e => new PreviewItem(e.Value.Length == 0 ? "(empty)" : e.Value, e.Describe()))
            .ToList();

        var problems = entries.Count(e => !e.IsHealthy);
        var summary = problems == 0
            ? $"{entries.Count} entries, all healthy."
            : $"{entries.Count} entries · {problems} need attention "
              + $"({entries.Count(e => e.Missing)} missing, {entries.Count(e => e.Duplicate)} duplicated, {entries.Count(e => e.Empty)} empty).";

        return Task.FromResult(new ToolPreview(items, summary));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var entries = Analyse(_readPath(), Directory.Exists);
        var bad = entries.Where(e => !e.IsHealthy).ToList();

        if (bad.Count == 0)
        {
            return Task.FromResult(ToolResult.Success("PATH is healthy",
                $"All {entries.Count} entries point at folders that exist, with no duplicates and no empty entries."));
        }

        var lines = bad
            .Select(e => $"{(e.Value.Length == 0 ? "(empty)" : e.Value)} — {e.Describe()}")
            .Concat(new[]
            {
                "",
                "osXos does not edit PATH. To fix these: Win+R, SystemPropertiesAdvanced, Environment Variables.",
            })
            .ToArray();

        return Task.FromResult(ToolResult.Success(
            $"{bad.Count} PATH {(bad.Count == 1 ? "entry needs" : "entries need")} attention", lines));
    }
}
