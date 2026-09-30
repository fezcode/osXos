namespace OsXos.Tools.Windows;

/// <summary>
/// One per-user DWORD that switches a piece of Explorer behaviour on or off, and the
/// words to explain it. The small Explorer toggles differ only in these, so they are
/// one class configured twice rather than two near-identical classes.
/// </summary>
public sealed record ExplorerToggle(
    string Slug,
    string Name,
    string Summary,
    string IconKey,
    string KeyPath,
    string ValueName,
    // What the feature is called in the preview row: "Seconds on the taskbar clock".
    string Feature,
    string OnText,
    string OffText,
    // The WM_SETTINGCHANGE area to broadcast after writing.
    string BroadcastArea,
    string Afterwards,
    IReadOnlyList<ToolStep> Steps);

/// <summary>
/// Flips one Explorer DWORD between 1 and 0, tells open windows, and says how to undo
/// it — which is running it again. A value that was never written counts as off,
/// which is Windows' own default for both of the toggles built from this.
/// </summary>
public sealed class ExplorerToggleTool : ITool, IHasState
{
    readonly ExplorerToggle _toggle;
    readonly IUserRegistry _registry;

    public ExplorerToggleTool(ExplorerToggle toggle, IUserRegistry registry)
    {
        _toggle = toggle;
        _registry = registry;
    }

    public string Id => "windows." + _toggle.Slug;
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => _toggle.Name;
    public string Summary => _toggle.Summary;
    public string IconKey => _toggle.IconKey;
    public string? Warning => null;
    public bool IsDestructive => false;
    public IReadOnlyList<ToolStep> Steps => _toggle.Steps;

    bool On => _registry.GetDword(_toggle.KeyPath, _toggle.ValueName) is { } v && v != 0;

    public Task<ToolState> ReadStateAsync(CancellationToken ct) =>
        Task.FromResult(On ? new ToolState("On", StateTone.On) : new ToolState("Off", StateTone.Off));

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var raw = _registry.GetDword(_toggle.KeyPath, _toggle.ValueName);
        var on = raw is { } v && v != 0;
        var target = on ? 0 : 1;

        var items = new[]
        {
            new PreviewItem(_toggle.Feature,
                $"{(on ? "on" : "off")} → {(on ? "off" : "on")}   ({_toggle.ValueName} = {(raw?.ToString() ?? "not set")} → {target})"),
            new PreviewItem("Setting", @"HKEY_CURRENT_USER\" + _toggle.KeyPath),
        };

        return Task.FromResult(new ToolPreview(items, on ? _toggle.OffText : _toggle.OnText));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var turningOn = !On;

        try
        {
            _registry.SetDword(_toggle.KeyPath, _toggle.ValueName, turningOn ? 1 : 0);
            _registry.BroadcastSettingChange(_toggle.BroadcastArea);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure("Could not change the setting", ex.Message));
        }

        return Task.FromResult(ToolResult.Success(
            $"{_toggle.Feature}: {(turningOn ? "on" : "off")}",
            _toggle.Afterwards,
            "Run this tool again to switch back."));
    }

    // ------------------------------------------------------------ the toggles --

    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string CabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";

    public static readonly ExplorerToggle ClockSeconds = new(
        Slug: "clock-seconds",
        Name: "Show Seconds on the Taskbar Clock",
        Summary: "Toggle whether the clock in the taskbar shows seconds as well as hours and minutes.",
        IconKey: "IconInfo",
        KeyPath: Advanced,
        ValueName: "ShowSecondsInSystemClock",
        Feature: "Seconds on the taskbar clock",
        OnText: "Will show seconds on the taskbar clock.",
        OffText: "Will stop showing seconds on the taskbar clock.",
        BroadcastArea: "TraySettings",
        Afterwards: "The taskbar has been told to update. If the clock does not change within a few seconds, Restart Explorer applies it.",
        Steps: new ToolStep[]
        {
            new("Read the current setting",
                @"ShowSecondsInSystemClock under HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced. 1 shows seconds; 0, or no value at all, shows hours and minutes only — Windows' default."),
            new("Flip it",
                "The same value Settings → Time & language → Date & time changes on recent Windows 11 builds, set the same way. Run the tool twice and you are back where you started."),
            new("Tell the taskbar",
                "A settings-change broadcast asks the taskbar to re-read it. On some Windows 10 builds the clock only updates when Explorer restarts; the Restart Explorer tool does that."),
            new("Only your account is affected",
                "A per-user value. No administrator rights, and nothing else about the taskbar changes. Windows notes that a ticking clock costs a little battery on laptops."),
        });

    public static readonly ExplorerToggle FullPathTitle = new(
        Slug: "full-path-title",
        Name: "Show Full Path in Explorer Titles",
        Summary: "Toggle whether File Explorer names its windows by full path — C:\\Users\\you\\Documents — or just the folder name.",
        IconKey: "IconFolder",
        KeyPath: CabinetState,
        ValueName: "FullPath",
        Feature: "Full path in File Explorer window titles",
        OnText: "Will name File Explorer windows by their full path.",
        OffText: "Will name File Explorer windows by folder name only.",
        BroadcastArea: "ShellState",
        Afterwards: "Folder windows you open from now on use the new title. Windows already open keep theirs until they navigate or reopen.",
        Steps: new ToolStep[]
        {
            new("Read the current setting",
                @"FullPath under HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState — the ""Display the full path in the title bar"" box in Folder Options. 1 is on; 0 or no value is off, the default."),
            new("Flip it",
                "Run the tool twice and you are back where you started."),
            new("Where the title shows",
                "Windows 11's File Explorer hides its title bar behind tabs, so there the full path appears where the window's name does: the taskbar preview, Alt+Tab and Task View — which is exactly where two folders called src are otherwise impossible to tell apart."),
            new("Only your account is affected",
                "A per-user value. No administrator rights, and nothing else about Explorer changes."),
        });
}
