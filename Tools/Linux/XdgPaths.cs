namespace OsXos.Tools.Linux;

/// <summary>
/// The XDG base directories, resolved the way the specification says. .NET's
/// SpecialFolder does not cover the cache directory at all on Linux and maps
/// LocalApplicationData to ~/.local/share, so these are worked out here rather than
/// guessed at each call site.
/// </summary>
public static class XdgPaths
{
    public static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>$XDG_CACHE_HOME, or ~/.cache when it is unset or not absolute.</summary>
    public static string CacheHome => Resolve("XDG_CACHE_HOME", ".cache");

    /// <summary>$XDG_DATA_HOME, or ~/.local/share when it is unset or not absolute.</summary>
    public static string DataHome => Resolve("XDG_DATA_HOME", Path.Combine(".local", "share"));

    static string Resolve(string variable, string fallbackUnderHome)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        // The spec says a relative value must be ignored, not resolved against cwd.
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)
            ? value
            : Path.Combine(Home, fallbackUnderHome);
    }
}
