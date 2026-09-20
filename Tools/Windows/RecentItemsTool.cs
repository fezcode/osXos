namespace OsXos.Tools.Windows;

/// <summary>
/// Clears the recent-documents list and the jump lists behind it. Three folders,
/// all under the user's own roaming profile, and all of them a record of what has
/// been opened on this machine rather than anything an application needs.
/// </summary>
public sealed class RecentItemsTool : ITool
{
    /// <summary>Relative to %APPDATA%. AutomaticDestinations is what a taskbar jump
    /// list reads; CustomDestinations is the pinned half an application supplies.</summary>
    public const string RecentSubPath = @"Microsoft\Windows\Recent";

    readonly string _recentDir;

    public RecentItemsTool() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), RecentSubPath))
    {
    }

    public RecentItemsTool(string recentDir) => _recentDir = recentDir;

    public string Id => "windows.recent-items";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Privacy;
    public string Name => "Clear Recent Files & Jump Lists";
    public string Summary => "Forget which documents were opened recently, including the taskbar jump lists.";
    public string IconKey => "IconPrivacy";
    public string? Warning => "Pinned items in jump lists are stored alongside the recent ones and go too. Applications keep their own history — this clears what Windows remembers, not what each program does.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Find what Windows is remembering",
            @"Three folders under %APPDATA%\Microsoft\Windows\Recent. The folder itself holds a shortcut per recently opened document; AutomaticDestinations holds the jump lists you get by right-clicking a taskbar icon; CustomDestinations holds the entries an application supplies for itself."),
        new("Delete the shortcuts and jump lists",
            "Only those three folders are touched, and only their contents. Not one of your actual documents is opened, moved or deleted — these are shortcuts and index files, nothing more."),
        new("Some may be held open",
            "Explorer keeps a handle on a jump list it is currently showing. Those are skipped rather than forced, counted, and listed afterwards. Closing open Explorer windows first clears more."),
        new("Windows starts a fresh list",
            "Recent documents and jump lists rebuild as you use the machine. If you want them to stay empty, turn the feature off in Settings → Personalisation → Start rather than clearing it repeatedly."),
    };

    /// <summary>The three folders this clears, in the order the Review stage lists them.</summary>
    public IReadOnlyList<string> Folders => new[]
    {
        _recentDir,
        Path.Combine(_recentDir, "AutomaticDestinations"),
        Path.Combine(_recentDir, "CustomDestinations"),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = new List<PreviewItem>();

        foreach (var folder in Folders)
        {
            // Top-level files only. The Recent folder contains the other two as
            // subdirectories, and sweeping recursively would list them twice.
            foreach (var item in FileSweep.Files(folder, "*"))
            {
                ct.ThrowIfCancellationRequested();
                items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                "Nothing to clear — Windows is not holding any recent documents or jump lists."));
        }

        var total = items.Sum(i => i.Bytes ?? 0);
        return Task.FromResult(new ToolPreview(items,
            $"{items.Count:N0} entr{(items.Count == 1 ? "y" : "ies")} · {FileSweep.FormatBytes(total)} · {_recentDir}"));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var sweep = FileSweep.Delete(preview.Items, progress, ct);
        var lines = new List<string>
        {
            $"Cleared {sweep.Deleted:N0} of {preview.Items.Count:N0} entries.",
        };

        if (sweep.Skipped > 0)
        {
            lines.Add($"{sweep.Skipped} left in place — held open by Explorer.");
            lines.AddRange(sweep.Problems);
        }

        if (sweep.Deleted == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be cleared", lines.ToArray()));

        lines.Add("Recent documents and jump lists will rebuild as you use the machine.");
        return Task.FromResult(ToolResult.Success(
            $"Cleared {sweep.Deleted:N0} recent entries", lines.ToArray()));
    }
}
