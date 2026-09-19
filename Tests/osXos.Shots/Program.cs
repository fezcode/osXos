using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using OsXos;
using OsXos.Models;
using OsXos.Services;
using OsXos.Tools;
using OsXos.ViewModels;
using OsXos.Views;

namespace OsXos.Shots;

/// <summary>
/// Renders the real windows off-screen and writes them to PNG. Every shot goes
/// through the same XAML the app uses, so what lands in out/ is what the app draws.
/// </summary>
static class Program
{
    static string OutDir = "";

    [STAThread]
    static void Main(string[] args)
    {
        OutDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(OutDir);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .AfterSetup(_ => Capture())
            .SetupWithoutStarting();

        Console.WriteLine($"shots written to {OutDir}");
    }

    static void Capture()
    {
        var services = new AppServices();

        // Every palette, on the page with the most surface types on it.
        foreach (var theme in new[] { "default", "harbor-night", "blueprint", "orchid-dusk" })
        {
            // The view model has to be built first: SettingsViewModel's constructor
            // applies the saved theme, so applying one before this would be undone.
            var vm = new MainWindowViewModel(services);
            ThemeManager.ApplyTheme(ThemeCatalog.FindTheme(theme));
            ThemeManager.ApplyFont(ThemeCatalog.FindFont("neo-grotesque"));

            Shoot($"main-{theme}", new MainWindow { DataContext = vm }, 1180, 760);
        }

        ThemeManager.ApplyTheme(ThemeCatalog.FindTheme("default"));

        // Settings, and the main window with About open.
        var settingsVm = new MainWindowViewModel(services);
        settingsVm.SelectedNav = settingsVm.NavItems[^1];
        Shoot("settings", new MainWindow { DataContext = settingsVm }, 1180, 760);

        var aboutVm = new MainWindowViewModel(services);
        aboutVm.OpenAboutCommand.Execute().Subscribe(_ => { });
        Shoot("about", new MainWindow { DataContext = aboutVm }, 1180, 760);

        // Search results.
        var searchVm = new MainWindowViewModel(services);
        searchVm.Query = "cache";
        Shoot("search", new MainWindow { DataContext = searchVm }, 1180, 760);

        // The tool window, at each of its three stages.
        var tool = services.Tools.Tools.First(t => t.Id == "windows.icon-cache");

        var explain = new ToolWindowViewModel(tool, services.OS, alwaysExplain: true);
        Shoot("tool-explain", new ToolWindow { DataContext = explain }, 660, 580);

        var review = new ToolWindowViewModel(tool, services.OS, alwaysExplain: false);
        Pump();
        Shoot("tool-review", new ToolWindow { DataContext = review }, 660, 580);

        // A blocked preview: the honest "nothing to do here" path.
        var blocked = new ToolWindowViewModel(
            new BlockedStub(), services.OS, alwaysExplain: false);
        Pump();
        Shoot("tool-blocked", new ToolWindow { DataContext = blocked }, 660, 580);
    }

    /// <summary>Lets the inspection tasks queued on the dispatcher finish.</summary>
    static void Pump()
    {
        for (var i = 0; i < 40; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    static void Shoot(string name, Window window, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        window.Show();
        Pump();

        var frame = window.CaptureRenderedFrame();
        var path = Path.Combine(OutDir, name + ".png");
        using (var stream = File.Create(path)) frame?.Save(stream);
        window.Close();

        Console.WriteLine($"  {name}.png");
    }

    /// <summary>Stands in for a tool whose preview blocks, to shoot that state.</summary>
    sealed class BlockedStub : ITool
    {
        public string Id => "stub.blocked";
        public OSKind Platform => OSKind.Windows;
        public ToolCategory Category => ToolCategory.Network;
        public string Name => "Flush DNS Cache";
        public string Summary => "Discard remembered DNS lookups so names resolve fresh.";
        public string IconKey => "IconNetwork";
        public string? Warning => null;
        public bool IsDestructive => false;

        public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
        {
            new("Check for systemd-resolved", "osXos looks for resolvectl before it runs anything."),
        };

        public Task<ToolPreview> InspectAsync(CancellationToken ct) => Task.FromResult(
            ToolPreview.Blocked(
                "systemd-resolved does not appear to be in use — resolvectl is not installed. If this machine caches DNS at all it is doing so through something else (dnsmasq, nscd or unbound), each of which is cleared differently, so osXos will not guess."));

        public Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
