namespace OsXos.Tools.Linux;

/// <summary>
/// Clears ~/.cache, listing each application's directory with its size. The broad
/// version of <see cref="ThumbnailCacheTool"/>, kept separate because most people
/// want the thumbnails gone far more often than they want the whole cache gone.
/// </summary>
public sealed class UserCacheTool : ITool
{
    readonly string _cacheDir;

    public UserCacheTool() : this(XdgPaths.CacheHome) { }

    public UserCacheTool(string cacheDir) => _cacheDir = cacheDir;

    public string Id => "linux.user-cache";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Clear User Cache";
    public string Summary => "Empty ~/.cache, where applications accumulate their scratch data.";
    public string IconKey => "IconTrash";
    public string? Warning => "Close your applications first. Deleting a cache under a running program can make it misbehave until restarted, and browsers will sign you out of some sites.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Look in your XDG cache directory",
            "$XDG_CACHE_HOME, which is ~/.cache unless you have moved it. By the freedesktop specification everything in here is, by definition, data an application can regenerate — that is the whole contract of the directory."),
        new("Size each application's directory",
            "Every entry is measured so you can see where the space went before deciding. Browser profiles, package-manager downloads, font caches and build tools are usually the large ones."),
        new("Delete what can be deleted",
            "Anything held open by a running program is skipped rather than forced, and reported afterwards. This is why closing your applications first reclaims noticeably more. No root and no polkit prompt: this is all your own."),
        new("Applications rebuild what they need",
            "The cost is that the next launch of each affected program is slower while it repopulates, and some websites will ask you to sign in again. Nothing in ~/.config or ~/.local/share — your actual settings and data — is touched."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = FileSweep.Children(_cacheDir).ToList();
        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"Nothing to clean — {_cacheDir} is empty or does not exist."));
        }

        var total = items.Sum(i => i.Bytes ?? 0);
        return Task.FromResult(new ToolPreview(items,
            $"{items.Count} item{(items.Count == 1 ? "" : "s")} · {FileSweep.FormatBytes(total)} to reclaim · {_cacheDir}"));
    }

    public Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct)
    {
        var sweep = FileSweep.Delete(preview.Items);
        var lines = new List<string>
        {
            $"Deleted {sweep.Deleted} of {preview.Items.Count} items, reclaiming {FileSweep.FormatBytes(sweep.Freed)}.",
        };
        if (sweep.Skipped > 0)
        {
            lines.Add($"{sweep.Skipped} left in place — in use by a running program.");
            lines.AddRange(sweep.Problems);
        }

        if (sweep.Deleted == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be deleted", lines.ToArray()));

        return Task.FromResult(ToolResult.Success(
            $"User cache cleared — {FileSweep.FormatBytes(sweep.Freed)} reclaimed", lines.ToArray()));
    }
}
