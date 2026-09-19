using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OsXos.Tools.Windows;

/// <summary>
/// The two Explorer "Advanced" values this tool flips, behind an interface so the
/// preview text and the decision logic can be tested without touching the registry.
/// </summary>
public interface IExplorerAdvancedSettings
{
    /// <summary>Explorer's <c>Hidden</c>: 1 shows hidden files, 2 hides them.</summary>
    int Hidden { get; set; }

    /// <summary>Explorer's <c>HideFileExt</c>: 0 shows extensions, 1 hides them.</summary>
    int HideFileExt { get; set; }

    /// <summary>Tells the shell to re-read the values, so open windows update.</summary>
    void NotifyShell();
}

[SupportedOSPlatform("windows")]
public sealed class RegistryExplorerSettings : IExplorerAdvancedSettings
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public int Hidden
    {
        get => Read("Hidden", 2);
        set => Write("Hidden", value);
    }

    public int HideFileExt
    {
        get => Read("HideFileExt", 1);
        set => Write("HideFileExt", value);
    }

    static int Read(string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    static void Write(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    const int SHCNE_ASSOCCHANGED = 0x08000000;
    const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    /// <summary>
    /// Without this the registry values change but every open Explorer window keeps
    /// rendering the old state until it is restarted, which reads to the user as the
    /// tool having silently failed.
    /// </summary>
    public void NotifyShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
}

/// <summary>
/// Shows or hides hidden files and known file extensions in File Explorer. A plain
/// two-way toggle: the preview names the current state and the state it will move to,
/// and running it again puts everything back.
/// </summary>
public sealed class HiddenFilesTool : ITool
{
    readonly IExplorerAdvancedSettings _settings;

    public HiddenFilesTool(IExplorerAdvancedSettings settings) => _settings = settings;

    public string Id => "windows.hidden-files";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Show Hidden Files & Extensions";
    public string Summary => "Toggle File Explorer between hiding and showing hidden items and known file extensions.";
    public string IconKey => "IconEye";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read what Explorer is doing now",
            @"Two values under HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced decide this: Hidden (1 shows hidden files, 2 hides them) and HideFileExt (0 shows extensions, 1 hides them). osXos reads both before it changes anything."),
        new("Flip both to the opposite state",
            "If either is currently hiding something, both are switched to show. If both are already showing, both are switched back to hide. The tool is its own undo — run it twice and you are exactly where you started."),
        new("Tell Explorer to re-read them",
            "A SHChangeNotify broadcast makes every open File Explorer window pick the change up immediately. Without it the setting is saved but nothing on screen changes until you sign out."),
        new("Only your account is affected",
            "These are per-user values under HKEY_CURRENT_USER. No administrator rights are needed, no other account on this PC sees any difference, and no file is created, moved or deleted."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var hidden = _settings.Hidden;
        var hideExt = _settings.HideFileExt;

        var showingHidden = hidden == 1;
        var showingExt = hideExt == 0;
        var turningOn = !(showingHidden && showingExt);

        var items = new[]
        {
            new PreviewItem(
                "Hidden files and folders",
                $"{(showingHidden ? "shown" : "hidden")} → {(turningOn ? "shown" : "hidden")}   (Hidden = {hidden} → {(turningOn ? 1 : 2)})"),
            new PreviewItem(
                "Known file extensions",
                $"{(showingExt ? "shown" : "hidden")} → {(turningOn ? "shown" : "hidden")}   (HideFileExt = {hideExt} → {(turningOn ? 0 : 1)})"),
        };

        var summary = turningOn
            ? "Will show hidden files and file extensions in File Explorer."
            : "Will hide hidden files and file extensions in File Explorer.";

        return Task.FromResult(new ToolPreview(items, summary));
    }

    public Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct)
    {
        var turningOn = !(_settings.Hidden == 1 && _settings.HideFileExt == 0);

        try
        {
            _settings.Hidden = turningOn ? 1 : 2;
            _settings.HideFileExt = turningOn ? 0 : 1;
            _settings.NotifyShell();
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure("Could not update Explorer", ex.Message));
        }

        var state = turningOn ? "shown" : "hidden";
        return Task.FromResult(ToolResult.Success(
            turningOn ? "Hidden files and extensions are now shown" : "Hidden files and extensions are now hidden",
            $"Hidden files: {state}.",
            $"File extensions: {state}.",
            "Open File Explorer windows have been told to refresh. Run this tool again to switch back."));
    }
}
