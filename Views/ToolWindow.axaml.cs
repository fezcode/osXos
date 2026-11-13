using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using OsXos.Services;
using OsXos.ViewModels;

namespace OsXos.Views;

/// <summary>
/// The one window every tool opens in. It carries no tool-specific code: the stages,
/// the steps, the found list and the result all come from the view model, so adding a
/// tool never means touching a view.
/// </summary>
public partial class ToolWindow : Window
{
    public ToolWindow()
    {
        InitializeComponent();
        Opened += (_, _) => FitTheatreToScreen();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ToolWindowViewModel vm) vm.CloseRequested += Close;
        };

        // A window closed mid-inspection would otherwise leave the scan running
        // against a view model nothing is watching any more.
        Closed += (_, _) =>
        {
            if (DataContext is ToolWindowViewModel vm) vm.Cancel();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    void OnTheatreTitlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    void FitTheatreToScreen()
    {
        if (!ThemeManager.Appearance.IsTheatre || Screens.ScreenFromWindow(Owner ?? this) is not { } screen) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        SetCurrentValue(WidthProperty, Math.Min(Width, area.Width / scale - 24));
        SetCurrentValue(HeightProperty, Math.Min(Height, area.Height / scale - 24));
        Position = new PixelPoint(
            Math.Clamp(Position.X, area.X + 12, Math.Max(area.X + 12, area.Right - (int)(Width * scale) - 12)),
            Math.Clamp(Position.Y, area.Y + 12, Math.Max(area.Y + 12, area.Bottom - (int)(Height * scale) - 12)));
    }
}
