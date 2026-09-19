using Avalonia;
using Avalonia.Media;
using ReactiveUI;

namespace OsXos.ViewModels;

public class ViewModelBase : ReactiveObject
{
}

/// <summary>
/// Resolving the icon geometries out of App.axaml. The views bind to a StreamGeometry
/// rather than naming a resource key in XAML, because the icon for a card or a nav
/// row is chosen by the tool, not by the template.
/// </summary>
public static class Icons
{
    static readonly StreamGeometry Empty = new();

    public static StreamGeometry Get(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetResource(key, null, out var value) == true &&
                value is StreamGeometry geometry)
            {
                return geometry;
            }
        }
        catch
        {
            // Resolved before the app's resources exist — the designer, or a test.
        }
        return Empty;
    }

    public static IBrush Brush(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetResource(key, null, out var value) == true &&
                value is IBrush brush)
            {
                return brush;
            }
        }
        catch
        {
            // As above.
        }
        return Brushes.Transparent;
    }
}
