namespace OsXos.Tools.MacOS;

/// <summary>
/// Clears ~/Library/Caches. Per-application folders, listed with their sizes so it is
/// clear what is about to go — this folder is routinely the largest reclaimable thing
/// on a Mac.
/// </summary>
public sealed class UserCachesTool : ITool
{
    readonly string _cachesDir;

    public UserCachesTool() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches"))
    {
    }

    public UserCachesTool(string cachesDir) => _cachesDir = cachesDir;

    public string Id => "macos.user-caches";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Clear User Caches";
    public string Summary => "Empty ~/Library/Caches, where applications accumulate their scratch data.";
    public string IconKey => "IconTrash";
    public string? Warning => "Quit your applications first. Deleting a cache under a running app can make it behave oddly until it is restarted, and browsers will sign you out of some sites.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Look in your own Caches folder",
            "~/Library/Caches holds one folder per application, keyed by bundle identifier. It belongs to your account, so no administrator rights are needed and no other user is affected. The system-wide /Library/Caches is not touched."),
        new("Size each application's folder",
            "Every entry is measured so you can see where the space actually went before deciding. Browsers, Xcode and media applications are usually the large ones."),
        new("Delete what can be deleted",
            "Folders held open by a running application are skipped rather than forced, and reported afterwards. This is why quitting your apps first reclaims noticeably more."),
        new("Applications rebuild what they need",
            "A cache is by definition reproducible — that is what makes it a cache. The cost is that the next launch of each affected app is slower while it repopulates, and some websites will ask you to sign in again."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = FileSweep.Children(_cachesDir, ct).ToList();
        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"Nothing to clean — {_cachesDir} is empty or does not exist."));
        }

        var total = items.Sum(i => i.Bytes ?? 0);
        return Task.FromResult(new ToolPreview(items,
            $"{items.Count} item{(items.Count == 1 ? "" : "s")} · {FileSweep.FormatBytes(total)} to reclaim · {_cachesDir}"));
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
            lines.Add($"{sweep.Skipped} left in place — in use by a running application.");
            lines.AddRange(sweep.Problems);
        }

        if (sweep.Deleted == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be deleted", lines.ToArray()));

        return Task.FromResult(ToolResult.Success(
            $"User caches cleared — {FileSweep.FormatBytes(sweep.Freed)} reclaimed", lines.ToArray()));
    }
}
