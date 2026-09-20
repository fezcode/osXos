namespace OsXos.Tools.Windows;

/// <summary>
/// Clears the three lists Explorer keeps of what you have typed: addresses typed
/// into the address bar, terms typed into the search box, and commands typed into
/// the Run dialog. All under HKEY_CURRENT_USER, so nothing here needs elevation and
/// no other account is affected.
/// </summary>
public sealed class ExplorerHistoryTool : ITool
{
    const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

    /// <summary>
    /// The three keys and what to call them. WordWheelQuery holds the Explorer
    /// search box's history, which is the one people are usually surprised exists.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Sources = new[]
    {
        ($@"{ExplorerKey}\TypedPaths", "Address bar history"),
        ($@"{ExplorerKey}\WordWheelQuery", "Explorer search history"),
        ($@"{ExplorerKey}\RunMRU", "Run dialog history"),
    };

    readonly IRegistryAccess _registry;

    public ExplorerHistoryTool(IRegistryAccess registry) => _registry = registry;

    public string Id => "windows.explorer-history";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Privacy;
    public string Name => "Clear Explorer & Run History";
    public string Summary => "Forget addresses typed into Explorer, terms typed into its search box, and Run commands.";
    public string IconKey => "IconSearch";
    public string? Warning => "The dropdown suggestions you may rely on go with it — Explorer will stop offering paths you type often until you have typed them again.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the three lists",
            @"All under HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer. TypedPaths is what you have typed into the address bar, WordWheelQuery is the Explorer search box, and RunMRU is the Win+R dialog. osXos reads them before changing anything, and the Review stage shows you the actual entries."),
        new("Delete each remembered value",
            "The values are removed one at a time; the keys themselves stay, because Windows expects them to exist. Nothing outside those three keys is read or written."),
        new("Only your account is affected",
            "These live in HKEY_CURRENT_USER. No administrator rights are needed and no other account on this PC sees any change."),
        new("Nothing is undone by this",
            "Clearing the history does not close, move or delete anything you visited — it only forgets that you typed it. Explorer starts remembering again from your next keystroke."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var items = new List<PreviewItem>();

        foreach (var (key, label) in Sources)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var name in _registry.ValueNames(RegHive.CurrentUser, key))
            {
                // RunMRU carries a "MRUList" value that is the ordering, not an
                // entry; it goes too, but showing the user its raw value would be
                // noise rather than information.
                var value = _registry.GetValue(RegHive.CurrentUser, key, name);
                var shown = name.Equals("MRUList", StringComparison.OrdinalIgnoreCase)
                    ? "(ordering)"
                    : value?.ToString() ?? "";

                items.Add(new PreviewItem($"{label} — {name}", shown));
            }
        }

        if (items.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                "Nothing to clear — Explorer is not remembering any typed paths, searches or Run commands."));
        }

        return Task.FromResult(new ToolPreview(items,
            $"{items.Count:N0} remembered entr{(items.Count == 1 ? "y" : "ies")} across three lists."));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        int cleared = 0, failed = 0;
        var lines = new List<string>();
        var total = Sources.Sum(s => _registry.ValueNames(RegHive.CurrentUser, s.Key).Count);
        var done = 0;

        foreach (var (key, label) in Sources)
        {
            ct.ThrowIfCancellationRequested();

            // Snapshot first: deleting while enumerating the live key is asking for
            // a half-cleared list.
            var names = _registry.ValueNames(RegHive.CurrentUser, key).ToList();
            var ok = 0;

            foreach (var name in names)
            {
                progress?.Report(new ToolProgress(++done, total, $"{label} — {name}"));
                if (_registry.DeleteValue(RegHive.CurrentUser, key, name)) ok++;
                else failed++;
            }

            cleared += ok;
            if (names.Count > 0) lines.Add($"{label}: cleared {ok} of {names.Count}.");
        }

        if (cleared == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be cleared", lines.ToArray()));

        if (failed > 0) lines.Add($"{failed} value(s) could not be removed.");
        lines.Add("Explorer starts remembering again from your next keystroke.");

        return Task.FromResult(ToolResult.Success(
            $"Cleared {cleared:N0} remembered entries", lines.ToArray()));
    }
}
