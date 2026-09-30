using Avalonia;
using Avalonia.ReactiveUI;
using System;
using System.Linq;

namespace OsXos;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, FreeDesktop... or any other
    // standard .NET libraries until ApplicationMain is called: entropy causes fast
    // things to go wrong.
    [STAThread]
    public static void Main(string[] args)
    {
        // Hide the Taskbar's keeper: no window, keeps the taskbar hidden until told to stop.
        if (OperatingSystem.IsWindows() && args.Contains(Tools.Windows.TaskbarKeeper.Argument))
        {
            Tools.Windows.WindowsTaskbarKeeperProcess.RunHere();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
        .UseReactiveUI();
}
