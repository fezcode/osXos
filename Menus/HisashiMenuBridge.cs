using Avalonia.Threading;
using OsXos.Services;

namespace OsXos.Menus;

/// <summary>
/// The Windows half of the menu bar. Windows has no system menu bar to put anything
/// in, so osXos publishes its menus to Hisashi over the hoswl pipe instead.
///
/// Entirely optional and entirely quiet: if Hisashi is not running the client retries
/// in the background and nothing else in the app notices.
/// </summary>
public sealed class HisashiMenuBridge : IMenuBarBridge
{
    readonly AppMenuModel _model;
    readonly HoswlClient _client;

    public HisashiMenuBridge(AppMenuModel model, string version)
    {
        _model = model;
        _client = new HoswlClient("com.fezcode.osxos", "osXos", version);
        _client.Clicked += id => Dispatcher.UIThread.Post(() => _model.Invoke(id));
    }

    public void Start(bool enabled)
    {
        _client.SetEnabled(enabled);
        Refresh();
        _client.Start();
    }

    /// <summary>Follows the Settings checkbox; the connection itself stays up.</summary>
    public void SetEnabled(bool on) => _client.SetEnabled(on);

    /// <summary>
    /// Resends the tree so the checkmarks match what the window shows. Cheap enough
    /// that patching single rows is not worth the bookkeeping.
    /// </summary>
    public void Refresh() => _client.SetMenus(_model.Build().Select(ToHoswl).ToList());

    /// <summary>
    /// hoswl's node shape is close enough to <see cref="AppMenuNode"/> that this is a
    /// rename, but the protocol has its own conventions: a separator is a row with
    /// <c>sep</c>, and <c>enabled</c> is only sent when it is false.
    /// </summary>
    public static HoswlNode ToHoswl(AppMenuNode node)
    {
        if (node.IsSeparator) return HoswlNode.Separator();

        if (node.IsSubmenu)
        {
            return new HoswlNode
            {
                Id = node.Id,
                Label = node.Label,
                Items = node.Items!.Select(ToHoswl).ToList(),
            };
        }

        return HoswlNode.Item(node.Id!, node.Label!, node.Shortcut, node.Check, node.Enabled);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
