using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OsXos.Tools.Windows;

/// <summary>
/// The two values behind Settings → Personalization → Colors → "Choose your mode",
/// behind an interface so the toggle logic can be tested without touching the
/// registry. Both are 1 for light and 0 for dark; Windows' "Custom" mode is simply
/// the two disagreeing.
/// </summary>
public interface IWindowsAppearance
{
    /// <summary><c>AppsUseLightTheme</c>: what applications draw in.</summary>
    int AppsUseLightTheme { get; set; }

    /// <summary><c>SystemUsesLightTheme</c>: the taskbar, Start, and notifications.</summary>
    int SystemUsesLightTheme { get; set; }

    /// <summary>Tells every open window the colour set changed, so it redraws now.</summary>
    void Broadcast();
}

[SupportedOSPlatform("windows")]
public sealed class RegistryWindowsAppearance : IWindowsAppearance
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // A value that has never been written means Windows' factory default, which is light.
    public int AppsUseLightTheme
    {
        get => Read("AppsUseLightTheme");
        set => Write("AppsUseLightTheme", value);
    }

    public int SystemUsesLightTheme
    {
        get => Read("SystemUsesLightTheme");
        set => Write("SystemUsesLightTheme", value);
    }

    static int Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) is int v ? v : 1;
        }
        catch
        {
            return 1;
        }
    }

    static void Write(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    const int HWND_BROADCAST = 0xFFFF;
    const int WM_SETTINGCHANGE = 0x001A;
    const int SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    /// <summary>
    /// "ImmersiveColorSet" is the notification Settings itself sends when you change
    /// mode. The taskbar, File Explorer and every app that follows the system theme
    /// listen for it; without it the values are saved but nothing redraws until the
    /// next sign-in. Abort-if-hung, so one frozen window cannot stall the tool.
    /// </summary>
    public void Broadcast() =>
        SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero,
            "ImmersiveColorSet", SMTO_ABORTIFHUNG, 3000, out _);
}

/// <summary>
/// Switches Windows between dark and light mode — both halves of it, apps and the
/// taskbar together. A two-way toggle: the preview names the mode now and the mode
/// it will move to, and running it again puts everything back.
/// </summary>
public sealed class DarkModeTool : ITool, IHasState
{
    readonly IWindowsAppearance _appearance;

    public DarkModeTool(IWindowsAppearance appearance) => _appearance = appearance;

    public string Id => "windows.dark-mode";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.System;
    public string Name => "Switch Dark / Light Mode";
    public string Summary => "Flip Windows between dark and light mode — apps, the taskbar and Start together.";
    public string IconKey => "IconMoon";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the mode Windows is in now",
            @"Two values under HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize decide it: AppsUseLightTheme for applications and SystemUsesLightTheme for the taskbar, Start and notifications. 1 is light, 0 is dark, and a value that was never written means light."),
        new("Switch both to the other mode",
            "If both are dark, both become light. Anything else — light, or Windows' Custom mode where the two disagree — becomes fully dark. Run it twice and you are back in the mode you started from, with apps and taskbar matching."),
        new("Tell every window to redraw",
            "The same ImmersiveColorSet broadcast Settings sends. The taskbar, File Explorer and apps that follow the system theme switch on the spot; an app that only reads the mode at startup picks it up the next time it opens."),
        new("Only your account is affected",
            "Per-user values under HKEY_CURRENT_USER. No administrator rights, no other account on this PC sees a difference, and your wallpaper, accent colour and theme are left as they are."),
    };

    /// <summary>Fully dark means both halves dark; the toggle goes to light only from there.</summary>
    static bool IsDark(int apps, int system) => apps == 0 && system == 0;

    static string Mode(int value) => value == 0 ? "dark" : "light";

    public Task<ToolState> ReadStateAsync(CancellationToken ct)
    {
        var apps = _appearance.AppsUseLightTheme;
        var system = _appearance.SystemUsesLightTheme;
        return Task.FromResult(apps == system
            ? new ToolState(apps == 0 ? "Dark" : "Light", apps == 0 ? StateTone.On : StateTone.Off)
            : new ToolState("Custom", StateTone.Partial, $"Apps are {Mode(apps)}, the taskbar is {Mode(system)}."));
    }

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var apps = _appearance.AppsUseLightTheme;
        var system = _appearance.SystemUsesLightTheme;
        var target = IsDark(apps, system) ? 1 : 0;

        var items = new[]
        {
            new PreviewItem("Apps",
                $"{Mode(apps)} → {Mode(target)}   (AppsUseLightTheme = {apps} → {target})"),
            new PreviewItem("Taskbar, Start and notifications",
                $"{Mode(system)} → {Mode(target)}   (SystemUsesLightTheme = {system} → {target})"),
        };

        var now = apps == system ? $"Windows is in {Mode(apps)} mode" : "Windows is in Custom mode";
        return Task.FromResult(new ToolPreview(items, $"{now}. Will switch to {Mode(target)} mode."));
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var target = IsDark(_appearance.AppsUseLightTheme, _appearance.SystemUsesLightTheme) ? 1 : 0;

        try
        {
            _appearance.AppsUseLightTheme = target;
            _appearance.SystemUsesLightTheme = target;
            _appearance.Broadcast();
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Failure("Could not change the Windows mode", ex.Message));
        }

        return Task.FromResult(ToolResult.Success(
            $"Windows is now in {Mode(target)} mode",
            $"Apps: {Mode(target)}. Taskbar, Start and notifications: {Mode(target)}.",
            "Open windows have been told to redraw. An app that only checks at startup follows the next time it opens.",
            "Run this tool again to switch back."));
    }
}
