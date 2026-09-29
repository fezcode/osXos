using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OsXos.Tools.Windows;

/// <summary>
/// Writing to HKEY_CURRENT_USER, for the toggle tools, behind an interface so their
/// logic is tested against an in-memory hive. <see cref="IRegistryAccess"/> covers
/// reading and deleting across both hives; this is the narrower half that changes
/// things, and it only ever reaches the current user's own settings.
/// </summary>
public interface IUserRegistry
{
    /// <summary>A DWORD value, or null when it or its key is absent.</summary>
    int? GetDword(string keyPath, string name);

    void SetDword(string keyPath, string name, int value);

    bool KeyExists(string keyPath);

    /// <summary>Creates the key (and its parents) with an empty default value.</summary>
    void CreateKeyWithEmptyDefault(string keyPath);

    /// <summary>Deletes a key and everything under it. Missing is not an error.</summary>
    void DeleteKeyTree(string keyPath);

    /// <summary>
    /// Sends WM_SETTINGCHANGE naming <paramref name="area"/>, the notification the
    /// Settings app sends when it changes the same values, so open windows re-read them.
    /// </summary>
    void BroadcastSettingChange(string area);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsUserRegistry : IUserRegistry
{
    public int? GetDword(string keyPath, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(name) is int v ? v : null;
        }
        catch
        {
            return null;
        }
    }

    public void SetDword(string keyPath, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public bool KeyExists(string keyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public void CreateKeyWithEmptyDefault(string keyPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue("", "", RegistryValueKind.String);
    }

    public void DeleteKeyTree(string keyPath) =>
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);

    const int HWND_BROADCAST = 0xFFFF;
    const int WM_SETTINGCHANGE = 0x001A;
    const int SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    public void BroadcastSettingChange(string area) =>
        SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero,
            area, SMTO_ABORTIFHUNG, 3000, out _);
}
