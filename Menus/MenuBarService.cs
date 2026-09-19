using Avalonia.Controls;
using OsXos.Services;
using OsXos.Tools;
using OsXos.ViewModels;
using ReactiveUI;

namespace OsXos.Menus;

/// <summary>
/// Picks the right menu bar for the platform and keeps it in step with the window.
///
/// The menu itself is described once in <see cref="AppMenuModel"/>; all this decides
/// is where it gets drawn, which is the one thing that genuinely differs:
///
///   macOS   the system menu bar, attached to the application
///   Windows Hisashi's menubar over the hoswl pipe, since Windows offers nothing
///   Linux   the window's DBus export, where the desktop has a global menu
/// </summary>
public sealed class MenuBarService : IAsyncDisposable
{
    readonly IMenuBarBridge? _bridge;

    public MenuBarService(MainWindowViewModel vm, Window window, AppServices services, string version)
    {
        var model = new AppMenuModel(vm, services.Tools, services.OS);

        _bridge = services.OS switch
        {
            OSKind.MacOS => new NativeMenuBridge(model, window: null),
            OSKind.Linux => new NativeMenuBridge(model, window),
            _ => new HisashiMenuBridge(model, version),
        };

        // The tree carries checkmarks for the current page and the state of the
        // search box, so it has to follow the app rather than be sent once.
        model.Changed += () => _bridge.Refresh();
        vm.WhenAnyValue(x => x.SelectedNav).Subscribe(_ => _bridge.Refresh());
        vm.WhenAnyValue(x => x.HasQuery).Subscribe(_ => _bridge.Refresh());

        vm.Settings.MenuBarChanged = on => _bridge.SetEnabled(on);
    }

    /// <summary>
    /// Starts publishing. Never throws: a menu bar osXos could not offer is not a
    /// reason to fail startup.
    /// </summary>
    public static MenuBarService? TryStart(
        MainWindowViewModel vm, Window window, AppServices services, string version)
    {
        try
        {
            var service = new MenuBarService(vm, window, services, version);
            service._bridge!.Start(services.Settings.MenuBar);
            return service;
        }
        catch
        {
            return null;
        }
    }

    public ValueTask DisposeAsync() => _bridge?.DisposeAsync() ?? ValueTask.CompletedTask;
}
