namespace OsXos.Tools.Windows;

/// <summary>
/// Clears the per-extension "Open with" lists Explorer has built up. The fix for an
/// Open with menu offering programs that are long gone, or refusing to offer one
/// that is installed.
///
/// Scoped carefully: it removes only the FileExts cache under HKEY_CURRENT_USER,
/// which Windows rebuilds. It does not touch the machine-wide associations under
/// HKEY_CLASSES_ROOT, so no file type is unregistered.
/// </summary>
public sealed class OpenWithCacheTool : ITool
{
    public const string FileExtsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    readonly IRegistryAccess _registry;
    readonly IShellController _shell;

    public OpenWithCacheTool(IRegistryAccess registry, IShellController shell)
    {
        _registry = registry;
        _shell = shell;
    }

    public string Id => "windows.open-with-cache";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Rebuild Open With Lists";
    public string Summary => "Clear the per-extension Open with history so Explorer rebuilds it.";
    public string IconKey => "IconShell";
    public string? Warning => "Your per-extension defaults are part of this cache. After running it, some file types will open with the system default again until you set them back, and Explorer restarts.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Find the cache",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts holds one subkey per file extension, recording which programs you have opened it with and which one you chose as the default."),
        new("Why it goes wrong",
            "The list is only ever added to. Uninstall a program and its entry stays, so Open with keeps offering something that is not there; and a stale UserChoice can stop a newly installed program being offered at all."),
        new("Delete the per-extension subkeys",
            "Only the subkeys under FileExts are removed. The machine-wide associations under HKEY_CLASSES_ROOT are not touched, so no file type is unregistered and no program is uninstalled — Windows keeps opening files, just with its own defaults again."),
        new("Restart Explorer so it re-reads them",
            "The shell caches this in memory, so without a restart nothing appears to change. Explorer is ended and started again; open folder windows close. Your per-extension choices are gone and will need setting again — that is the cost of rebuilding the list, and the reason this is not something to run casually."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var extensions = _registry.SubKeyNames(RegHive.CurrentUser, FileExtsKey);

        if (extensions.Count == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                "Nothing to clear — Explorer has no per-extension Open with history on this account."));
        }

        var items = extensions
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .Select(ext => new PreviewItem(ext, DescribeChoice(ext)))
            .ToList();

        return Task.FromResult(new ToolPreview(items,
            $"{items.Count:N0} file extension{(items.Count == 1 ? "" : "s")} · Explorer will restart"));
    }

    /// <summary>
    /// What the user actually chose for this extension, so the Review stage shows
    /// what is being given up rather than a bare list of extensions.
    /// </summary>
    string DescribeChoice(string extension)
    {
        var choice = _registry.GetValue(
            RegHive.CurrentUser, $@"{FileExtsKey}\{extension}\UserChoice", "ProgId")?.ToString();

        if (!string.IsNullOrEmpty(choice)) return $"default: {choice}";

        var recent = _registry.ValueNames(
            RegHive.CurrentUser, $@"{FileExtsKey}\{extension}\OpenWithList").Count;

        return recent > 0 ? $"{recent} remembered program(s)" : "no default set";
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var extensions = preview.Items.Select(i => i.Label).ToList();
        int cleared = 0, failed = 0;
        var done = 0;

        foreach (var ext in extensions)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ToolProgress(++done, extensions.Count, ext));

            if (_registry.DeleteSubKeyTree(RegHive.CurrentUser, FileExtsKey, ext)) cleared++;
            else failed++;
        }

        var lines = new List<string>
        {
            $"Cleared {cleared:N0} of {extensions.Count:N0} extensions.",
        };
        if (failed > 0) lines.Add($"{failed} could not be removed.");

        if (cleared == 0)
            return ToolResult.Failure("Nothing could be cleared", lines.ToArray());

        progress?.Report(new ToolProgress(0, 0, "Restarting Windows Explorer..."));
        await _shell.StopExplorerAsync(ct).ConfigureAwait(false);
        var restarted = await _shell.StartExplorerAsync(ct).ConfigureAwait(false);

        lines.Add(restarted
            ? "Windows Explorer restarted."
            : "Windows Explorer did not come back. Press Ctrl+Shift+Esc, then File → Run new task → explorer.exe");
        lines.Add("Open with lists rebuild as you use each file type. Per-extension defaults need setting again.");

        return restarted
            ? ToolResult.Success($"Rebuilt Open with lists for {cleared:N0} extensions", lines.ToArray())
            : ToolResult.Failure("Lists cleared, but Explorer did not restart", lines.ToArray());
    }
}
