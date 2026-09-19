using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(services) };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
