namespace OsXos.Tools;

/// <summary>
/// Everything that makes one sweep tool different from another: what it is called,
/// what it explains, and the map of places it is allowed to touch. The map is a
/// function of the roots so a test can point it at a fake profile, and so Run can
/// rebuild exactly what Inspect measured.
/// </summary>
public sealed record LocationJob(
    string Slug,
    OSKind Platform,
    ToolCategory Category,
    string Name,
    string Summary,
    string IconKey,
    string Warning,
    IReadOnlyList<ToolStep> Steps,
    Func<ProfileRoots, IReadOnlyList<Location>> Map,
    string NothingFound,
    string Afterwards);

/// <summary>
/// A tool that measures a fixed list of known locations and — once you have seen the
/// list — deletes them. Developer caches, browser caches, shader caches, crash dumps
/// and the rest are all this shape, so they are one class configured by a
/// <see cref="LocationJob"/> rather than one class each.
/// </summary>
public sealed class LocationCleanupTool : ITool
{
    readonly LocationJob _job;
    readonly ProfileRoots _roots;

    public LocationCleanupTool(LocationJob job) : this(job, ProfileRoots.Current(job.Platform)) { }

    public LocationCleanupTool(LocationJob job, ProfileRoots roots)
    {
        _job = job;
        _roots = roots;
    }

    public string Id => _job.Platform switch
    {
        OSKind.Windows => "windows." + _job.Slug,
        OSKind.MacOS => "macos." + _job.Slug,
        _ => "linux." + _job.Slug,
    };

    public OSKind Platform => _job.Platform;
    public ToolCategory Category => _job.Category;
    public string Name => _job.Name;
    public string Summary => _job.Summary;
    public string IconKey => _job.IconKey;
    public string? Warning => _job.Warning;
    public bool IsDestructive => true;
    public IReadOnlyList<ToolStep> Steps => _job.Steps;

    /// <summary>The map this tool works from on these roots. Public for the tests.</summary>
    public IReadOnlyList<Location> Map() => _job.Map(_roots);

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = LocationSweep.Rows(Map(), ct);
        if (items.Count == 0) return Task.FromResult(ToolPreview.Blocked(_job.NothingFound));

        var products = items
            .Select(i => i.Label.Split(" · ")[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var summary =
            $"{items.Count:N0} location{(items.Count == 1 ? "" : "s")} · " +
            $"{FileSweep.FormatBytes(items.Sum(i => i.Bytes ?? 0))} to reclaim · " +
            string.Join(", ", products);

        return Task.FromResult(new ToolPreview(items, summary));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var sweep = LocationSweep.Delete(Map(), preview.Items, progress, ct);
        var locations = $"{preview.Items.Count:N0} location{(preview.Items.Count == 1 ? "" : "s")}";

        var lines = new List<string>
        {
            $"Removed {sweep.Deleted:N0} item{(sweep.Deleted == 1 ? "" : "s")} across {locations}, reclaiming {FileSweep.FormatBytes(sweep.Freed)}.",
        };

        if (sweep.Skipped > 0)
        {
            lines.Add($"{sweep.Skipped:N0} left in place — held open by a running program.");
            lines.AddRange(sweep.Problems);
        }

        if (sweep.Deleted == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be removed", lines.ToArray()));

        lines.Add(_job.Afterwards);
        return Task.FromResult(ToolResult.Success(
            $"{FileSweep.FormatBytes(sweep.Freed)} reclaimed from {locations}", lines.ToArray()));
    }
}
