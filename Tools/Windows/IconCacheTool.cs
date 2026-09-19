using System.Diagnostics;

namespace OsXos.Tools.Windows;

/// <summary>
/// Stopping and starting Explorer, behind an interface so the icon cache tool's
/// inspection and reporting can be tested without the desktop disappearing.
/// </summary>
public interface IShellController
{
    bool IsExplorerRunning { get; }
    Task<int> StopExplorerAsync(CancellationToken ct);
    Task<bool> StartExplorerAsync(CancellationToken ct);
}

public sealed class ExplorerController : IShellController
{
    public bool IsExplorerRunning => Process.GetProcessesByName("explorer").Length > 0;

    /// <summary>Ends every Explorer process and returns how many it ended.</summary>
    public async Task<int> StopExplorerAsync(CancellationToken ct)
    {
        var procs = Process.GetProcessesByName("explorer");
        int stopped = 0;
        foreach (var p in procs)
        {
            try
            {
                p.Kill();
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
                stopped++;
            }
            catch
            {
                // Already gone, or not ours to end. Either way the file may now be free.
            }
            finally
            {
                p.Dispose();
            }
        }

        // Killing Explorer is not instantaneous from the filesystem's point of view:
        // the handles on the cache databases are released as the process tears down,
        // and deleting too soon just fails with a sharing violation.
        if (stopped > 0) await Task.Delay(700, ct).ConfigureAwait(false);
        return stopped;
    }

    public async Task<bool> StartExplorerAsync(CancellationToken ct)
    {
        try
        {
            // Explorer re-attaches to the running shell if one exists, so starting it
            // when it is already up is harmless.
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            return false;
        }

        // Give the shell a moment to paint before the result stage claims it is back.
        for (var i = 0; i < 20; i++)
        {
            if (IsExplorerRunning) return true;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        return IsExplorerRunning;
    }
}

/// <summary>
/// Clears the Windows icon and thumbnail caches. The databases are held open by
/// explorer.exe, so the only way to remove them is to end the shell, delete, and
/// start it again — which is why this tool warns before it runs.
/// </summary>
public sealed class IconCacheTool : ITool
{
    /// <summary>Where Windows 10/11 keeps the caches, relative to %LocalAppData%.</summary>
    public const string ExplorerCacheSubPath = @"Microsoft\Windows\Explorer";

    readonly string _cacheDir;
    readonly string _legacyDb;
    readonly IShellController _shell;

    public IconCacheTool() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ExplorerCacheSubPath),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IconCache.db"),
        new ExplorerController())
    {
    }

    public IconCacheTool(string cacheDir, string legacyDb, IShellController shell)
    {
        _cacheDir = cacheDir;
        _legacyDb = legacyDb;
        _shell = shell;
    }

    public string Id => "windows.icon-cache";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Clear Icon Cache";
    public string Summary => "Delete the icon and thumbnail databases so Windows redraws every icon from scratch.";
    public string IconKey => "IconImage";
    public string? Warning => "Windows Explorer will close and restart. Your desktop and taskbar disappear for a second, and any open File Explorer windows are closed.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Close Windows Explorer",
            "The cache databases are held open by explorer.exe for as long as it runs, and Windows will not let anything delete a file that is in use. Ending the shell releases those handles. Your desktop, taskbar and any open folder windows close with it."),
        new("Delete the cache databases",
            @"Removes iconcache_*.db and thumbcache_*.db from %LocalAppData%\Microsoft\Windows\Explorer, plus the legacy %LocalAppData%\IconCache.db left behind by older Windows versions. Nothing else in that folder is touched."),
        new("Start Windows Explorer again",
            "The desktop and taskbar come back. osXos waits until the shell is actually running before it reports success, so you are not left staring at an empty screen."),
        new("Windows rebuilds the cache on demand",
            "There is nothing to restore. Each icon is re-read from its source the first time something asks for it, which is what fixes icons that had gone blank, stale or wrong. Folders you browse a lot will feel very slightly slower once, then normal again."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = FileSweep.Files(_cacheDir, "iconcache_*.db", "thumbcache_*.db").ToList();

        if (File.Exists(_legacyDb))
        {
            try
            {
                items.Add(new PreviewItem(Path.GetFileName(_legacyDb), _legacyDb, new FileInfo(_legacyDb).Length));
            }
            catch
            {
                // Unreadable: leave it out rather than promise to delete it.
            }
        }

        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                "No icon or thumbnail cache files were found. Windows has not built one yet, or it has already been cleared."));
        }

        var summary = $"{items.Count} file{(items.Count == 1 ? "" : "s")} · {FileSweep.FormatBytes(items.Sum(i => i.Bytes ?? 0))} to reclaim";
        return Task.FromResult(new ToolPreview(items, summary));
    }

    public async Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct)
    {
        var lines = new List<string>();

        var stopped = await _shell.StopExplorerAsync(ct).ConfigureAwait(false);
        lines.Add(stopped > 0
            ? $"Closed Windows Explorer ({stopped} process{(stopped == 1 ? "" : "es")})."
            : "Windows Explorer was not running.");

        var sweep = FileSweep.Delete(preview.Items);
        lines.Add($"Deleted {sweep.Deleted} of {preview.Items.Count} files, reclaiming {FileSweep.FormatBytes(sweep.Freed)}.");
        if (sweep.Skipped > 0)
        {
            lines.Add($"{sweep.Skipped} could not be deleted — still in use by another process.");
            lines.AddRange(sweep.Problems);
        }

        var restarted = await _shell.StartExplorerAsync(ct).ConfigureAwait(false);
        lines.Add(restarted
            ? "Windows Explorer restarted."
            : "Windows Explorer did not come back. Press Ctrl+Shift+Esc, then File → Run new task → explorer.exe");

        // The cache is gone either way, but leaving the user without a desktop is a
        // failure whatever the deletion count says.
        if (!restarted) return ToolResult.Failure("Cache cleared, but Explorer did not restart", lines.ToArray());
        if (sweep.Deleted == 0) return ToolResult.Failure("Nothing could be deleted", lines.ToArray());

        lines.Add("Windows will rebuild each icon the next time it needs it.");
        return ToolResult.Success(
            $"Icon cache cleared — {FileSweep.FormatBytes(sweep.Freed)} reclaimed", lines.ToArray());
    }
}
