using System.Runtime.Versioning;
using Microsoft.Win32;

namespace OsXos.Tools.Windows;

public enum RegHive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>
/// The slice of the registry the Windows tools need, behind an interface so their
/// logic can be tested without a real hive. Several of these tools delete things;
/// none of them should have to be trusted on a live machine to prove they pick the
/// right values.
///
/// Every member is failure-tolerant by design: a key that is not there is not an
/// error, it is a machine that never had the feature.
/// </summary>
public interface IRegistryAccess
{
    object? GetValue(RegHive hive, string keyPath, string name);

    /// <summary>Value names under a key, empty when the key does not exist.</summary>
    IReadOnlyList<string> ValueNames(RegHive hive, string keyPath);

    /// <summary>Subkey names under a key, empty when the key does not exist.</summary>
    IReadOnlyList<string> SubKeyNames(RegHive hive, string keyPath);

    bool DeleteValue(RegHive hive, string keyPath, string name);

    bool DeleteSubKeyTree(RegHive hive, string keyPath, string subKey);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    static RegistryKey Root(RegHive hive) =>
        hive == RegHive.LocalMachine ? Registry.LocalMachine : Registry.CurrentUser;

    public object? GetValue(RegHive hive, string keyPath, string name)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetValue(name);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> ValueNames(RegHive hive, string keyPath)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetValueNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> SubKeyNames(RegHive hive, string keyPath)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public bool DeleteValue(RegHive hive, string keyPath, string name)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath, writable: true);
            if (key == null) return false;
            key.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool DeleteSubKeyTree(RegHive hive, string keyPath, string subKey)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath, writable: true);
            if (key == null) return false;
            key.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
