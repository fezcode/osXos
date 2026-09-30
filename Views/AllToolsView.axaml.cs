using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OsXos.Views;

public partial class AllToolsView : UserControl
{
    public AllToolsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
