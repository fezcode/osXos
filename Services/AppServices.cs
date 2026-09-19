using OsXos.Tools;

namespace OsXos.Services;

/// <summary>Composition root: builds and wires everything once at startup.</summary>
public sealed class AppServices
{
    /// <summary>
    /// Where settings live. SpecialFolder.ApplicationData resolves per platform, so
    /// one expression gives %APPDATA%\fezcode\osxos on Windows,
    /// ~/Library/Application Support/fezcode/osxos on macOS and
    /// ~/.config/fezcode/osxos on Linux — each the right place for that OS.
    /// </summary>
    public string DataDir { get; }

    public SettingsService Settings { get; }
    public IProcessRunner Runner { get; }
    public ToolRegistry Tools { get; }
    public OSKind OS { get; }

    public AppServices()
    {
        DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fezcode", "osxos");

        try { Directory.CreateDirectory(DataDir); }
        catch { /* SettingsService falls back to defaults and reports a failed save */ }

        Settings = new SettingsService(DataDir);
        Runner = new ProcessRunner();
        OS = CategoryCatalog.CurrentOS;
        Tools = new ToolRegistry(OS, Runner);
    }
}
