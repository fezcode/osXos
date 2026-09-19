using System.Diagnostics;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using OsXos.Models;
using OsXos.Services;
using ReactiveUI;

namespace OsXos.ViewModels;

/// <summary>
/// The settings page, following Cogas's shape: theme and font apply live as you
/// click them, but nothing is written to disk until Save. Leaving the page dirty
/// raises the guard prompt in <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    readonly AppServices _services;

    // The last values written to disk. Dirty state is the difference between these
    // and what is on screen, so they move only when a Save actually succeeds.
    string _savedThemeKey;
    string _savedFontKey;
    bool _savedAlwaysExplain;

    public SettingsViewModel(AppServices services)
    {
        _services = services;

        _savedThemeKey = services.Settings.Theme;
        _savedFontKey = services.Settings.Font;
        _savedAlwaysExplain = services.Settings.AlwaysExplain;

        _selectedTheme = ThemeCatalog.FindTheme(_savedThemeKey);
        _selectedFont = ThemeCatalog.FindFont(_savedFontKey);
        _alwaysExplain = _savedAlwaysExplain;

        var version = typeof(SettingsViewModel).Assembly.GetName().Version;
        VersionText = $"osXos v{version?.ToString(3) ?? "dev"}";

        // Apply what was saved before anything is drawn.
        ThemeManager.ApplyTheme(_selectedTheme);
        ThemeManager.ApplyFont(_selectedFont);

        SaveCommand = ReactiveCommand.Create(Save);
        CancelCommand = ReactiveCommand.Create(Revert);
        OpenDataFolderCommand = ReactiveCommand.Create(OpenDataFolder);
        CopyDataPathCommand = ReactiveCommand.CreateFromTask(CopyDataPathAsync);
    }

    public string VersionText { get; }

    // ---- appearance ----

    public IReadOnlyList<ThemeDefinition> Themes => ThemeCatalog.Themes;

    ThemeDefinition _selectedTheme;
    public ThemeDefinition SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTheme, value);
            if (value != null) ThemeManager.ApplyTheme(value);
            UpdateDirtyState();
        }
    }

    public IReadOnlyList<FontDefinition> Fonts => ThemeCatalog.Fonts;

    FontDefinition _selectedFont;
    public FontDefinition SelectedFont
    {
        get => _selectedFont;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedFont, value);
            if (value != null) ThemeManager.ApplyFont(value);
            UpdateDirtyState();
        }
    }

    // ---- behaviour ----

    bool _alwaysExplain;
    public bool AlwaysExplain
    {
        get => _alwaysExplain;
        set
        {
            this.RaiseAndSetIfChanged(ref _alwaysExplain, value);
            UpdateDirtyState();
        }
    }

    // ---- local data ----

    public string DataDir => _services.DataDir;
    public string SettingsFilePath => _services.Settings.FilePath;

    public string StorageSummary
    {
        get
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return "Not written yet — save once to create it.";
                var info = new FileInfo(SettingsFilePath);
                return $"{info.Length} bytes · last written {info.LastWriteTime:d MMM yyyy HH:mm}";
            }
            catch
            {
                return "Could not read the settings file.";
            }
        }
    }

    public string ToolCountText
    {
        get
        {
            var n = _services.Tools.Tools.Count;
            var c = _services.Tools.Categories.Count;
            return $"{n} tool{(n == 1 ? "" : "s")} across {c} categor{(c == 1 ? "y" : "ies")} on this machine";
        }
    }

    // ---- dirty state ----

    bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    string _status = "";
    public string Status
    {
        get => _status;
        private set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => Status.Length > 0;

    void UpdateDirtyState() =>
        IsDirty = (SelectedTheme?.Key ?? "") != _savedThemeKey ||
                  (SelectedFont?.Key ?? "") != _savedFontKey ||
                  AlwaysExplain != _savedAlwaysExplain;

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenDataFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyDataPathCommand { get; }

    public void Save()
    {
        var theme = SelectedTheme?.Key ?? "default";
        var font = SelectedFont?.Key ?? "neo-grotesque";

        var ok = _services.Settings.Update(d => d with
        {
            Theme = theme,
            Font = font,
            AlwaysExplain = AlwaysExplain,
        });

        if (SelectedTheme != null) ThemeManager.ApplyTheme(SelectedTheme);
        if (SelectedFont != null) ThemeManager.ApplyFont(SelectedFont);

        // The baseline moves with what is now in memory whether or not the write
        // landed: the settings are live either way, and leaving the page permanently
        // dirty over an unwritable file would trap the user behind the nav guard.
        _savedThemeKey = theme;
        _savedFontKey = font;
        _savedAlwaysExplain = AlwaysExplain;
        IsDirty = false;

        Status = ok
            ? "Saved."
            : $"Could not write {SettingsFilePath}. Your choices apply for this session only.";

        this.RaisePropertyChanged(nameof(StorageSummary));
    }

    public void Revert()
    {
        SelectedTheme = ThemeCatalog.FindTheme(_savedThemeKey);
        SelectedFont = ThemeCatalog.FindFont(_savedFontKey);
        AlwaysExplain = _savedAlwaysExplain;

        ThemeManager.ApplyTheme(SelectedTheme);
        ThemeManager.ApplyFont(SelectedFont);

        Status = "";
        IsDirty = false;
    }

    void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            // UseShellExecute is what makes this open the file manager rather than
            // trying to execute the directory; it is also the only form that works
            // the same way on all three platforms.
            Process.Start(new ProcessStartInfo(DataDir) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Status = $"Could not open the folder: {ex.Message}";
        }
    }

    async Task CopyDataPathAsync()
    {
        var clipboard = Clipboard();
        if (clipboard == null)
        {
            Status = "No clipboard is available.";
            return;
        }

        await clipboard.SetTextAsync(DataDir);
        Status = "Path copied.";
    }

    static IClipboard? Clipboard() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow?.Clipboard;
}
