namespace OsXos.Tools.Windows;

/// <summary>
/// Empties the current user's temp folder. Deliberately scoped to %TEMP% and not
/// C:\Windows\Temp: the machine-wide folder needs administrator rights, and osXos
/// v1 does not ask for any.
/// </summary>
public sealed class TempFolderTool : ITool
{
    readonly string _tempDir;

    public TempFolderTool() : this(Path.GetTempPath()) { }

    public TempFolderTool(string tempDir) => _tempDir = tempDir;

    public string Id => "windows.temp-folder";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Empty Temp Folder";
    public string Summary => "Delete what installers and applications left behind in your personal temp folder.";
    public string IconKey => "IconTrash";
    public string? Warning => "Deleted files do not go to the Recycle Bin. Close any installer that is mid-run first — its working files are in here.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Look in your personal temp folder",
            @"That is %TEMP%, normally C:\Users\<you>\AppData\Local\Temp. It belongs to your account alone, so no administrator rights are needed and nothing another user owns is touched."),
        new("Skip anything currently in use",
            "Running programs keep files open in here — a browser mid-download, an installer mid-install, Office holding a recovery copy. Windows refuses to delete those, and so does osXos. They are counted and listed rather than forced."),
        new("Delete the rest",
            "Everything else is removed permanently. It does not go to the Recycle Bin, because temp files are exactly what the Recycle Bin is not for."),
        new("Nothing needs restoring",
            "Applications recreate whatever they need here on their next run. The only thing you lose is disk space that was already being wasted."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = FileSweep.Children(_tempDir, ct).ToList();

        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"Nothing to clean — {_tempDir} is already empty."));
        }

        var total = items.Sum(i => i.Bytes ?? 0);
        var summary = $"{items.Count} item{(items.Count == 1 ? "" : "s")} · {FileSweep.FormatBytes(total)} to reclaim · {_tempDir}";
        return Task.FromResult(new ToolPreview(items, summary));
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
            lines.Add($"{sweep.Skipped} left in place — in use by a running program.");
            lines.AddRange(sweep.Problems);
        }

        if (sweep.Deleted == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be deleted", lines.ToArray()));

        return Task.FromResult(ToolResult.Success(
            $"Temp folder emptied — {FileSweep.FormatBytes(sweep.Freed)} reclaimed", lines.ToArray()));
    }
}
