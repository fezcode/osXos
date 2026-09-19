using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MiniCommand = System.Windows.Input.ICommand;

namespace OsXos.Menus;

/// <summary>
/// The macOS half of the menu bar, and Linux's where the desktop exports one.
///
/// On macOS this is the real system menu bar along the top of the screen — the first
/// submenu becomes the application menu, which is why <see cref="AppMenuModel"/> puts
/// About, Settings and Quit first and names it after the app. Attaching to
/// <see cref="Application"/> rather than to a window is what makes macOS treat it that
/// way.
///
/// On Linux the same tree is attached to the main window, which Avalonia exports over
/// DBus for desktops with a global menu (Unity, KDE with the applet, GNOME with the
/// AppIndicator extension). On a desktop without one this does nothing at all, which
/// is the right outcome: osXos's window has custom chrome and no menu strip of its
/// own to fall back to.
/// </summary>
public sealed class NativeMenuBridge : IMenuBarBridge
{
    readonly AppMenuModel _model;
    readonly Window? _window;

    /// <summary>
    /// Pass a window on Linux to export over DBus; pass null on macOS to attach to
    /// the application, which is what puts the menu in the system bar.
    /// </summary>
    public NativeMenuBridge(AppMenuModel model, Window? window)
    {
        _model = model;
        _window = window;
    }

    bool _enabled = true;

    public void Start(bool enabled)
    {
        _enabled = enabled;
        Refresh();
    }

    public void SetEnabled(bool on)
    {
        _enabled = on;
        Refresh();
    }

    public void Refresh()
    {
        // Turning it off clears osXos's menus. On macOS the system then shows the
        // default bar Avalonia provides rather than nothing, so the app stays usable.
        var menu = _enabled ? Build(_model) : new NativeMenu();

        if (_window != null) NativeMenu.SetMenu(_window, menu);
        else if (Application.Current != null) NativeMenu.SetMenu(Application.Current, menu);
    }

    /// <summary>Translates the platform-neutral tree into Avalonia's native menu.</summary>
    public static NativeMenu Build(AppMenuModel model)
    {
        var menu = new NativeMenu();
        foreach (var node in model.Build()) menu.Items.Add(Convert(node, model));
        return menu;
    }

    static NativeMenuItemBase Convert(AppMenuNode node, AppMenuModel model)
    {
        if (node.IsSeparator) return new NativeMenuItemSeparator();

        var item = new NativeMenuItem(node.Label ?? "")
        {
            IsEnabled = node.Enabled,
        };

        if (node.IsSubmenu)
        {
            var submenu = new NativeMenu();
            foreach (var child in node.Items!) submenu.Items.Add(Convert(child, model));
            item.Menu = submenu;
            return item;
        }

        if (node.Check is { } check)
        {
            item.ToggleType = NativeMenuItemToggleType.CheckBox;
            item.IsChecked = check;
        }

        if (TryGesture(node.Shortcut) is { } gesture) item.Gesture = gesture;

        var id = node.Id;
        if (id != null) item.Command = new RelayCommand(() => model.Invoke(id));

        return item;
    }

    /// <summary>
    /// Turns a display hint like "Cmd+," into a gesture macOS will actually bind.
    /// A hint that will not parse is dropped rather than guessed at: a menu row with
    /// no shortcut is fine, one with the wrong shortcut is a trap.
    /// </summary>
    public static KeyGesture? TryGesture(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return null;

        // Avalonia's parser wants Key enum names, so the punctuation keys people
        // write literally in a menu have to be spelled out.
        var text = shortcut
            .Replace("Cmd+", "Meta+", StringComparison.OrdinalIgnoreCase)
            .Replace(",", "OemComma", StringComparison.Ordinal)
            .Replace(".", "OemPeriod", StringComparison.Ordinal);

        try { return KeyGesture.Parse(text); }
        catch { return null; }
    }

    sealed class RelayCommand : MiniCommand
    {
        readonly Action _run;
        public RelayCommand(Action run) => _run = run;

        // Nothing here is ever conditionally disabled through the command: the menu
        // is rebuilt with IsEnabled instead, so both bridges behave the same way.
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
