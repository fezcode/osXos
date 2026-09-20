namespace OsXos.Tools.Linux;

/// <summary>
/// Clears the freedesktop thumbnail cache — the Linux equivalent of the Windows
/// thumbnail databases. Every desktop that follows the spec (GNOME, KDE, XFCE,
/// Cinnamon) shares this one directory.
/// </summary>
public sealed class ThumbnailCacheTool : ITool
{
    readonly string _thumbnailsDir;

    public ThumbnailCacheTool() : this(Path.Combine(XdgPaths.CacheHome, "thumbnails")) { }

    public ThumbnailCacheTool(string thumbnailsDir) => _thumbnailsDir = thumbnailsDir;

    public string Id => "linux.thumbnail-cache";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Clear Thumbnail Cache";
    public string Summary => "Delete generated file thumbnails so your file manager regenerates them.";
    public string IconKey => "IconImage";
    public string? Warning => "Thumbnails are regenerated on demand, so the first browse through a large picture or video folder will be noticeably slower.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Find the freedesktop thumbnail cache",
            "It lives at $XDG_CACHE_HOME/thumbnails, which is ~/.cache/thumbnails unless you have moved it. Every spec-compliant desktop — GNOME, KDE, XFCE, Cinnamon — writes to this one place, so clearing it covers all of them at once."),
        new("Size each subdirectory",
            "normal/ and large/ hold the thumbnails themselves; fail/ records the files that could not be thumbnailed, which is the part worth clearing when a file manager stubbornly refuses to preview something it now could."),
        new("Delete them",
            "The directories are removed. This is your own cache under your own home directory — no root, no polkit prompt, and nothing another user owns is touched."),
        new("The file manager rebuilds as you browse",
            "No original file is affected in any way; only the generated previews are gone. They come back automatically the next time you open each folder."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = FileSweep.Children(_thumbnailsDir, ct).ToList();
        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"Nothing to clean — {_thumbnailsDir} is empty or does not exist."));
        }

        var total = items.Sum(i => i.Bytes ?? 0);
        return Task.FromResult(new ToolPreview(items,
            $"{items.Count} item{(items.Count == 1 ? "" : "s")} · {FileSweep.FormatBytes(total)} to reclaim · {_thumbnailsDir}"));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var sweep = FileSweep.Delete(preview.Items, progress, ct);
        var lines = new List<string>
        {
            $"Deleted {sweep.Deleted} of {preview.Items.Count} items, reclaiming {FileSweep.FormatBytes(sweep.Freed)}.",
        };
        if (sweep.Skipped > 0)
        {
            lines.Add($"{sweep.Skipped} could not be removed.");
            lines.AddRange(sweep.Problems);
        }

        if (sweep.Deleted == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be deleted", lines.ToArray()));

        lines.Add("Your file manager will regenerate thumbnails as you browse.");
        return Task.FromResult(ToolResult.Success(
            $"Thumbnail cache cleared — {FileSweep.FormatBytes(sweep.Freed)} reclaimed", lines.ToArray()));
    }
}
