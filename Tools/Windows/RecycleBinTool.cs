using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OsXos.Tools.Windows;

/// <summary>What the Recycle Bin currently holds.</summary>
public readonly record struct RecycleBinState(long Bytes, long Items);

/// <summary>
/// The Recycle Bin, behind an interface so the tool's reporting can be tested
/// without emptying the developer's actual bin.
/// </summary>
public interface IRecycleBin
{
    RecycleBinState Query();
    bool Empty();
}

[SupportedOSPlatform("windows")]
public sealed class ShellRecycleBin : IRecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    // Null for the root path means "every drive's bin", which is what the shell's
    // own Recycle Bin shows and therefore what the user means.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    const uint SHERB_NOCONFIRMATION = 0x00000001;
    const uint SHERB_NOPROGRESSUI = 0x00000002;
    const uint SHERB_NOSOUND = 0x00000004;

    public RecycleBinState Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        try
        {
            return SHQueryRecycleBin(null, ref info) == 0
                ? new RecycleBinState(info.i64Size, info.i64NumItems)
                : new RecycleBinState(0, 0);
        }
        catch
        {
            return new RecycleBinState(0, 0);
        }
    }

    public bool Empty()
    {
        try
        {
            // osXos has already asked on its Review stage and drawn its own
            // progress, so Windows' confirmation and progress dialogs would be a
            // second prompt for a decision already taken.
            return SHEmptyRecycleBin(
                IntPtr.Zero, null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Empties the Recycle Bin across every drive, after showing what is in it. The one
/// tool here whose whole point is that the deletion is already deliberate — these
/// files were thrown away once; this makes it final.
/// </summary>
public sealed class RecycleBinTool : ITool
{
    readonly IRecycleBin _bin;

    public RecycleBinTool(IRecycleBin bin) => _bin = bin;

    public string Id => "windows.recycle-bin";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Empty Recycle Bin";
    public string Summary => "Permanently remove everything already thrown away, across every drive.";
    public string IconKey => "IconTrash";
    public string? Warning => "This is the point of no return for these files. Once the bin is emptied they cannot be restored from Windows — only from a backup.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Ask Windows what the bin holds",
            "SHQueryRecycleBin reports the total size and item count across every drive, which is what the shell's own Recycle Bin shows. Reading it changes nothing and takes no time."),
        new("Empty it",
            "SHEmptyRecycleBin, with Windows' own confirmation and progress dialogs suppressed — osXos has already asked on the Review stage, and a second prompt for a decision you have already made is just noise."),
        new("Every drive, not just C:",
            "Each drive keeps its own Recycle Bin, including external and removable ones that are connected. All of them are emptied together, exactly as the shell does it."),
        new("This cannot be undone",
            "Deleting a file to the Recycle Bin is reversible; emptying the bin is not. Anything in here that still matters should be restored before you run this, not after."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var state = _bin.Query();

        if (state.Items == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                "The Recycle Bin is already empty."));
        }

        var items = new[]
        {
            new PreviewItem("Items in the Recycle Bin", $"{state.Items:N0} across all drives"),
            new PreviewItem("Space they occupy", FileSweep.FormatBytes(state.Bytes), state.Bytes),
        };

        return Task.FromResult(new ToolPreview(items,
            $"{state.Items:N0} item{(state.Items == 1 ? "" : "s")} · {FileSweep.FormatBytes(state.Bytes)} to reclaim permanently"));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var before = _bin.Query();
        progress?.Report(new ToolProgress(0, 0, "Emptying the Recycle Bin..."));

        if (!_bin.Empty())
        {
            return Task.FromResult(ToolResult.Failure("Could not empty the Recycle Bin",
                "Windows refused the request. A file may be held open, or the bin on a removable drive may be unavailable."));
        }

        // Ask again rather than assuming: a file locked by another process stays,
        // and reporting the requested figure would overstate what happened.
        var after = _bin.Query();
        var freed = Math.Max(0, before.Bytes - after.Bytes);
        var removed = Math.Max(0, before.Items - after.Items);

        var lines = new List<string>
        {
            $"Removed {removed:N0} of {before.Items:N0} items, reclaiming {FileSweep.FormatBytes(freed)}.",
        };
        if (after.Items > 0)
            lines.Add($"{after.Items:N0} item(s) remain — held open, or on a drive that is not available.");

        return Task.FromResult(ToolResult.Success(
            $"Recycle Bin emptied — {FileSweep.FormatBytes(freed)} reclaimed", lines.ToArray()));
    }
}
