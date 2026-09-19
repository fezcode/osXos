using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsXos.Services;

/// <summary>
/// Everything osXos remembers between runs. A flat record rather than a key/value
/// table: there are a handful of settings, none of them secret, and a JSON file a
/// user can read and edit is a feature for a utility app.
/// </summary>
public sealed record SettingsData
{
    public string Theme { get; init; } = "default";
    public string Font { get; init; } = "neo-grotesque";

    /// <summary>
    /// Whether a tool window opens on Explain. Off sends it straight to Review — the
    /// steps are still one Back away, never removed.
    /// </summary>
    public bool AlwaysExplain { get; init; } = true;

    /// <summary>
    /// Whether osXos publishes its menus to the platform's menu bar — the system bar
    /// on macOS, Hisashi on Windows, the desktop's DBus export on Linux. On by
    /// default: where there is nowhere to draw them it costs nothing and shows nothing.
    /// </summary>
    public bool MenuBar { get; init; } = true;

    /// <summary>Sidebar width in pixels, or null when it has never been dragged.</summary>
    public double? SidebarWidth { get; init; }
}

/// <summary>
/// Reads and writes <see cref="SettingsData"/> as JSON. Every failure mode — no file,
/// unreadable file, malformed file, unwritable directory — falls back to defaults
/// rather than throwing: a settings file is never a reason for a utility not to start.
/// </summary>
public sealed class SettingsService
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string FilePath { get; }

    SettingsData _data;

    public SettingsService(string dataDir)
    {
        FilePath = Path.Combine(dataDir, "settings.json");
        _data = Load();
    }

    public SettingsData Data => _data;

    SettingsData Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new SettingsData();
            return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(FilePath), Json) ?? new SettingsData();
        }
        catch
        {
            // Hand-edited into invalid JSON, or unreadable. Defaults, and the next
            // Save overwrites it with something valid.
            return new SettingsData();
        }
    }

    /// <summary>Applies a change and writes it out. Returns false if it could not be saved.</summary>
    public bool Update(Func<SettingsData, SettingsData> change)
    {
        _data = change(_data);
        return Save();
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, Json));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string Theme => _data.Theme;
    public string Font => _data.Font;
    public bool AlwaysExplain => _data.AlwaysExplain;
    public bool MenuBar => _data.MenuBar;
    public double? SidebarWidth => _data.SidebarWidth;
}
