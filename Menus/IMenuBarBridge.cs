namespace OsXos.Menus;

/// <summary>
/// Somewhere osXos's menus can be drawn. There is one implementation per platform
/// because the platforms genuinely differ: macOS has a real system menu bar, Windows
/// has nothing and borrows Hisashi's, and a Linux desktop may or may not export a
/// global menu over DBus.
/// </summary>
public interface IMenuBarBridge : IAsyncDisposable
{
    /// <summary>Publishes the menu for the first time.</summary>
    void Start(bool enabled);

    /// <summary>Follows the Settings checkbox.</summary>
    void SetEnabled(bool on);

    /// <summary>Rebuilds the tree so checkmarks and enabled states match the window.</summary>
    void Refresh();
}
