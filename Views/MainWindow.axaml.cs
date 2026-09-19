using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OsXos.ViewModels;

namespace OsXos.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Windows adds an ~8px off-screen border when maximized in extended client area
        // mode. Adjusting the margin when maximized keeps edges and padding pixel-perfect.
        this.GetObservable(WindowStateProperty).Subscribe(state =>
        {
            var root = this.FindControl<Grid>("RootGrid");
            if (root != null)
                root.Margin = state == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        });

        // The view model is attached after the constructor runs, so both the remembered
        // sidebar width and the tool-window hook have to be applied when it arrives.
        DataContextChanged += (_, _) =>
        {
            ApplySidebarWidth();
            if (DataContext is MainWindowViewModel vm) vm.ShowToolWindow = ShowToolWindowAsync;
        };
    }

    /// <summary>
    /// Opens a tool's own window, modal to this one. Every tool shares the window;
    /// only its view model differs.
    /// </summary>
    async Task ShowToolWindowAsync(Tools.ITool _, ToolWindowViewModel vm)
    {
        var window = new ToolWindow { DataContext = vm };
        await window.ShowDialog(this);
    }

    ColumnDefinition? SidebarColumn =>
        this.FindControl<Grid>("RootGrid") is { ColumnDefinitions.Count: > 0 } root
            ? root.ColumnDefinitions[0]
            : null;

    /// <summary>Sizes the sidebar column from settings and hands the splitter its bounds.</summary>
    void ApplySidebarWidth()
    {
        if (DataContext is not MainWindowViewModel vm || SidebarColumn is not { } column) return;

        column.MinWidth = MainWindowViewModel.MinSidebarWidth;
        column.MaxWidth = MainWindowViewModel.MaxSidebarWidth;
        column.Width = new GridLength(vm.SidebarWidth, GridUnitType.Pixel);
    }

    /// <summary>
    /// Remembers where the drag left the sidebar. The splitter writes an absolute
    /// width into the column, which is the value worth keeping.
    /// </summary>
    void OnSidebarResized(object? sender, VectorEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || SidebarColumn is not { } column) return;
        if (!column.Width.IsAbsolute) return;

        vm.SidebarWidth = column.Width.Value;
        // The view model clamps; if it disagreed with the drag, the column follows it.
        if (Math.Abs(vm.SidebarWidth - column.Width.Value) > 0.5)
            column.Width = new GridLength(vm.SidebarWidth, GridUnitType.Pixel);
    }

    void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void OnMaximizeClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Clicking the dimmed backdrop closes About; clicks on the card itself do not.</summary>
    void OnAboutScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        if (DataContext is MainWindowViewModel vm) vm.CloseAboutCommand.Execute().Subscribe();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel { ShowAbout: true } vm)
        {
            vm.CloseAboutCommand.Execute().Subscribe();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
