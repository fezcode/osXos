namespace OsXos.Tools.Windows;

/// <summary>
/// Lists everything set to start when you sign in, from the places Task Manager's
/// Startup tab reads, with whether each is enabled — and changes none of it.
///
/// Read-only on purpose. Half of these entries are machine-wide and need
/// administrator rights to change, and the ones that are not are better switched off
/// in Task Manager or Settings, where the switch is reversible with one click. This
/// shows the whole list in one place and says where to go.
/// </summary>
public sealed class StartupAppsTool : ITool
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    public const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    readonly IRegistryAccess _registry;
    readonly string _userStartup;
    readonly string _commonStartup;

    public StartupAppsTool(IRegistryAccess registry)
        : this(registry,
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)) { }

    public StartupAppsTool(IRegistryAccess registry, string userStartup, string commonStartup)
    {
        _registry = registry;
        _userStartup = userStartup;
        _commonStartup = commonStartup;
    }

    public string Id => "windows.startup-apps";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.System;
    public string Name => "Startup Apps Report";
    public string Summary => "List everything that starts when you sign in, and whether it is enabled — read-only.";
    public string IconKey => "IconPlay";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the Run keys",
            @"Software\Microsoft\Windows\CurrentVersion\Run in HKEY_CURRENT_USER (just you) and HKEY_LOCAL_MACHINE (everyone), plus the 32-bit WOW6432Node copy. Each value is one program and the command that starts it."),
        new("Read the Startup folders",
            "Your own Startup folder in the Start Menu, and the one shared by every account. Each shortcut in them starts with Windows."),
        new("Check what has been switched off",
            "When you disable something in Task Manager, Windows does not delete it — it records the choice under StartupApproved. osXos reads that too, so each entry is shown as enabled or disabled the way Task Manager would."),
        new("Change nothing",
            "This tool only reads. To switch something off, use Task Manager's Startup apps page or Settings → Apps → Startup: both are reversible with one click, and the machine-wide entries need administrator rights there anyway."),
    };

    /// <summary>
    /// StartupApproved stores a binary value whose first byte is even for enabled and
    /// odd for disabled. No value at all means never touched, which is enabled.
    /// </summary>
    public static bool? IsEnabled(object? approval) => approval switch
    {
        byte[] { Length: > 0 } bytes => bytes[0] % 2 == 0,
        _ => null,
    };

    sealed record Entry(string Name, string Where, string Command, bool Enabled);

    IEnumerable<Entry> Entries()
    {
        IEnumerable<Entry> FromRun(RegHive hive, string key, string approvedKey, string where) =>
            _registry.ValueNames(hive, key)
                .Where(n => n.Length > 0)
                .Select(n => new Entry(
                    n, where,
                    _registry.GetValue(hive, key, n)?.ToString() ?? "",
                    IsEnabled(_registry.GetValue(hive, approvedKey, n)) ?? true));

        IEnumerable<Entry> FromFolder(string folder, RegHive hive, string where)
        {
            string[] files;
            try { files = Directory.GetFiles(folder); }
            catch { yield break; }

            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                yield return new Entry(Path.GetFileNameWithoutExtension(file), where, file,
                    IsEnabled(_registry.GetValue(hive, ApprovedFolder, name)) ?? true);
            }
        }

        return FromRun(RegHive.CurrentUser, RunKey, ApprovedRun, "you · Run key")
            .Concat(FromFolder(_userStartup, RegHive.CurrentUser, "you · Startup folder"))
            .Concat(FromRun(RegHive.LocalMachine, RunKey, ApprovedRun, "everyone · Run key"))
            .Concat(FromRun(RegHive.LocalMachine, Run32Key, ApprovedRun32, "everyone · Run key (32-bit)"))
            .Concat(FromFolder(_commonStartup, RegHive.LocalMachine, "everyone · Startup folder"));
    }

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var entries = Entries().ToList();
        if (entries.Count == 0)
            return Task.FromResult(ToolPreview.Blocked("Nothing is set to start when you sign in."));

        var items = entries
            .Select(e => new PreviewItem(
                $"{e.Name}  ({(e.Enabled ? "enabled" : "disabled")})",
                $"{e.Where} — {e.Command}"))
            .ToList();

        var enabled = entries.Count(e => e.Enabled);
        return Task.FromResult(new ToolPreview(items,
            $"{entries.Count} startup entr{(entries.Count == 1 ? "y" : "ies")}, {enabled} enabled. Read-only — running this changes nothing."));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        // A report's "run" repeats what Review showed and points at where to act.
        var lines = preview.Items
            .Select(i => $"{i.Label} — {i.Detail}")
            .Concat(new[]
            {
                "Nothing was changed.",
                "To switch an entry off: Task Manager (Ctrl+Shift+Esc) → Startup apps, or Settings → Apps → Startup.",
            })
            .ToArray();

        return Task.FromResult(ToolResult.Success(preview.Summary.Split(". ")[0], lines));
    }
}
