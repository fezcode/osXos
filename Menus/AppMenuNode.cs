namespace OsXos.Menus;

/// <summary>
/// One row of osXos's menu, described once and independently of who draws it.
///
/// This exists because the same menu has to reach two completely different places:
/// the Hisashi menubar over a named pipe on Windows, and the real system menu bar on
/// macOS. Describing the menu twice would guarantee the two drifted, so both bridges
/// translate from this.
/// </summary>
public sealed record AppMenuNode(
    string? Id,
    string? Label,
    string? Shortcut = null,
    bool? Check = null,
    bool Enabled = true,
    IReadOnlyList<AppMenuNode>? Items = null,
    bool IsSeparator = false)
{
    public bool IsSubmenu => Items is { Count: > 0 };

    public static AppMenuNode Separator() => new(null, null, IsSeparator: true);

    public static AppMenuNode Item(
        string id, string label, string? shortcut = null, bool? check = null, bool enabled = true) =>
        new(id, label, shortcut, check, enabled);

    public static AppMenuNode Menu(string id, string label, params AppMenuNode[] items) =>
        new(id, label, Items: items);

    /// <summary>Every clickable id in this subtree, for the bridges and for tests.</summary>
    public IEnumerable<string> Ids()
    {
        if (Items is { } children)
        {
            foreach (var id in children.SelectMany(c => c.Ids())) yield return id;
            yield break;
        }
        if (!IsSeparator && Id is { } own) yield return own;
    }
}
