using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OsXos.Menus;
using OsXos.Services;
using OsXos.ViewModels;
using OsXos.Views;

namespace OsXos;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices();
            var vm = new MainWindowViewModel(services);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            // The menu bar is optional decoration on every platform, so TryStart
            // swallows its own failures: a menu osXos could not publish is never a
            // reason to fail startup.
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "dev";
            var menus = MenuBarService.TryStart(vm, window, services, version);
            if (menus != null)
            {
                desktop.ShutdownRequested += (_, _) =>
                    menus.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
