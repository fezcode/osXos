using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
}
