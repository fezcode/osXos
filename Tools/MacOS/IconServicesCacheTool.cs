namespace OsXos.Tools.MacOS;

/// <summary>
/// The macOS counterpart of the Windows icon cache tool. Icon Services keeps its
/// store under the user's Caches folder; removing it and restarting Dock and Finder
/// makes macOS redraw every icon.
/// </summary>
public sealed class IconServicesCacheTool : ITool
{
    /// <summary>Restarting the two processes that hold icons on screen.</summary>
    public static readonly ShellCommand KillDock = new("killall", "Dock");
    public static readonly ShellCommand KillFinder = new("killall", "Finder");

    readonly string _cachesDir;
    readonly IProcessRunner _runner;

    public IconServicesCacheTool(IProcessRunner runner)
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches"), runner)
    {
    }

    public IconServicesCacheTool(string cachesDir, IProcessRunner runner)
    {
        _cachesDir = cachesDir;
        _runner = runner;
    }

    public string Id => "macos.icon-services-cache";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Clear Icon Services Cache";
    public string Summary => "Remove the Icon Services store so macOS redraws every icon from its source.";
    public string IconKey => "IconImage";
    public string? Warning => "Dock and Finder will restart. The Dock vanishes for a second and every open Finder window closes.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Find the Icon Services store",
            "macOS caches rendered icons in ~/Library/Caches/com.apple.iconservices.store, with an older com.apple.IconServices folder alongside it on systems upgraded from earlier releases. Both belong to your account."),
        new("Delete the cached store",
            "The folders are removed. Only the Icon Services entries are touched — everything else in ~/Library/Caches is left exactly as it is; that is a separate tool."),
        new("Restart Dock and Finder",
            "killall Dock and killall Finder. Both are relaunched automatically by macOS within a second, holding no icons in memory any more. Open Finder windows are closed by this, so save anything mid-rename first."),
        new("Icons are re-rendered on demand",
            "There is nothing to restore. Each icon is rebuilt from the application bundle the first time it is needed, which is what fixes generic, stale or blank icons. Note this is the per-user store only: the system-wide copy under /Library/Caches needs administrator rights and osXos does not ask for any."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = new List<PreviewItem>();
        foreach (var name in new[] { "com.apple.iconservices.store", "com.apple.IconServices" })
        {
            var path = Path.Combine(_cachesDir, name);
            if (Directory.Exists(path))
                items.Add(new PreviewItem(name + "/", path, FileSweep.SizeOf(path)));
        }

        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"No Icon Services cache was found in {_cachesDir}. It has already been cleared, or macOS has not built one."));
        }

        items.Add(new PreviewItem("Then run", KillDock.Display));
        items.Add(new PreviewItem("Then run", KillFinder.Display));

        var total = items.Sum(i => i.Bytes ?? 0);
        return Task.FromResult(new ToolPreview(items,
            $"{FileSweep.FormatBytes(total)} to reclaim, then Dock and Finder restart."));
    }

    public async Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct)
    {
        // Only the sized entries are paths; the trailing two are the commands below.
        var sweep = FileSweep.Delete(preview.Items.Where(i => i.Bytes.HasValue));
        var lines = new List<string>
        {
            $"Deleted {sweep.Deleted} cache folder{(sweep.Deleted == 1 ? "" : "s")}, reclaiming {FileSweep.FormatBytes(sweep.Freed)}.",
        };
        if (sweep.Skipped > 0)
        {
            lines.Add($"{sweep.Skipped} could not be removed.");
            lines.AddRange(sweep.Problems);
        }

        foreach (var cmd in new[] { KillDock, KillFinder })
        {
            var outcome = await _runner.RunAsync(cmd, ct).ConfigureAwait(false);
            lines.Add(outcome.Ok
                ? $"{cmd.Display} — restarted."
                : $"{cmd.Display} — failed ({outcome.Message}).");
        }

        if (sweep.Deleted == 0)
            return ToolResult.Failure("Nothing could be deleted", lines.ToArray());

        lines.Add("macOS will re-render each icon the next time it needs it.");
        return ToolResult.Success(
            $"Icon Services cache cleared — {FileSweep.FormatBytes(sweep.Freed)} reclaimed", lines.ToArray());
    }
}
