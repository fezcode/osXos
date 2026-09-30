namespace OsXos.Tools.Windows;

/// <summary>
/// Brings back the full right-click menu on Windows 11 — the one Windows 10 had,
/// without the "Show more options" step — or puts the new one back. A two-way toggle.
///
/// The switch is the well-known per-user override: an empty InprocServer32 key under
/// one CLSID in HKEY_CURRENT_USER makes Explorer fail to load the new menu's handler
/// and fall back to the classic one. Deleting the key undoes it completely.
/// </summary>
public sealed class ClassicContextMenuTool : ITool, IHasState
{
    public const string ClsidKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    public const string ServerKey = ClsidKey + @"\InprocServer32";

    /// <summary>Windows 11's first build. Windows 10 already has the classic menu.</summary>
    public const int Windows11Build = 22000;

    readonly IUserRegistry _registry;
    readonly IShellController _shell;
    readonly Func<int> _build;

    public ClassicContextMenuTool(IUserRegistry registry, IShellController shell)
        : this(registry, shell, () => Environment.OSVersion.Version.Build) { }

    public ClassicContextMenuTool(IUserRegistry registry, IShellController shell, Func<int> build)
    {
        _registry = registry;
        _shell = shell;
        _build = build;
    }

    public string Id => "windows.classic-context-menu";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Classic Right-Click Menu";
    public string Summary => "Toggle Windows 11 between its compact right-click menu and the full classic one.";
    public string IconKey => "IconViewList";
    public string? Warning => "Explorer restarts so the change takes effect, closing any open File Explorer windows. Other applications are not affected.";
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Check this is Windows 11",
            "The compact menu with \"Show more options\" arrived in Windows 11. Windows 10 already shows the full menu, so there the tool has nothing to do and says so."),
        new("Read which menu is in use",
            @"The classic menu is on when HKEY_CURRENT_USER\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32 exists with an empty default value. That empty entry stops Explorer loading the new menu, and it falls back to the old one."),
        new("Add or remove that one key",
            "Turning the classic menu on creates the key; turning it off deletes the CLSID key osXos created, and nothing else. No system file is touched and no administrator rights are needed — it is your own account's setting."),
        new("Restart Explorer",
            "Explorer only reads this when it starts, so it is restarted once. The taskbar blinks for a second and open folder windows close. Run the tool again to switch back."),
    };

    bool ClassicOn => _registry.KeyExists(ServerKey);

    public Task<ToolState> ReadStateAsync(CancellationToken ct) => Task.FromResult(
        _build() < Windows11Build
            ? new ToolState("Not on this PC", StateTone.Unavailable, "Windows 10 already uses the classic menu.")
            : ClassicOn ? new ToolState("Classic", StateTone.On) : new ToolState("Windows 11", StateTone.Off));

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (_build() < Windows11Build)
            return Task.FromResult(ToolPreview.Blocked(
                "This is Windows 10, which already uses the full classic right-click menu. There is nothing to switch."));

        var on = ClassicOn;
        var items = new[]
        {
            new PreviewItem("Right-click menu",
                on ? "classic (full) → Windows 11 (compact)" : "Windows 11 (compact) → classic (full)"),
            new PreviewItem(on ? "Will delete" : "Will create", @"HKEY_CURRENT_USER\" + (on ? ClsidKey : ServerKey)),
            new PreviewItem("Then", "Restart Explorer so it picks the change up"),
        };

        return Task.FromResult(new ToolPreview(items, on
            ? "Will switch back to the Windows 11 right-click menu."
            : "Will switch to the full classic right-click menu."));
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var turningOn = !ClassicOn;

        try
        {
            if (turningOn) _registry.CreateKeyWithEmptyDefault(ServerKey);
            else _registry.DeleteKeyTree(ClsidKey);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure("Could not change the menu setting", ex.Message);
        }

        progress?.Report(new ToolProgress(0, 0, "Restarting Explorer..."));
        await _shell.StopExplorerAsync(ct).ConfigureAwait(false);
        var restarted = await _shell.StartExplorerAsync(ct).ConfigureAwait(false);

        var lines = new List<string>
        {
            turningOn
                ? "Right-clicking now shows the full menu straight away."
                : "Right-clicking now shows the compact Windows 11 menu, with the rest under Show more options.",
            restarted
                ? "Explorer was restarted."
                : "Explorer did not come back by itself — press Ctrl+Shift+Esc, choose Run new task, and enter explorer.",
            "Run this tool again to switch back.",
        };

        return ToolResult.Success(
            turningOn ? "The classic right-click menu is on" : "The Windows 11 right-click menu is back",
            lines.ToArray());
    }
}
