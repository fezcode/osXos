namespace OsXos.Tools;

/// <summary>
/// The tools available on one machine, grouped the way the sidebar needs them.
///
/// The rule that shapes this class: a category reaches the sidebar only once a tool
/// claims it. <see cref="CategoryCatalog"/> reserves six names per OS, but an empty
/// one is never drawn — so there are no "no tools yet" placeholders anywhere in the
/// app, and a category appears by itself the day its first tool is added.
/// </summary>
public sealed class ToolRegistry
{
    public OSKind OS { get; }

    /// <summary>Every tool for this OS, in catalogue order.</summary>
    public IReadOnlyList<ITool> Tools { get; }

    /// <summary>Categories that actually have a tool, in taxonomy order.</summary>
    public IReadOnlyList<CategoryInfo> Categories { get; }

    public ToolRegistry(OSKind os, IReadOnlyList<ITool> tools)
    {
        OS = os;
        Tools = tools;

        Categories = CategoryCatalog.For(os)
            .Where(c => tools.Any(t => t.Category == c.Category))
            .ToList();
    }

    public ToolRegistry(OSKind os, IProcessRunner runner)
        : this(os, ToolCatalog.For(os, runner)) { }

    public IReadOnlyList<ITool> InCategory(ToolCategory category) =>
        Tools.Where(t => t.Category == category).ToList();

    /// <summary>
    /// Free-text match over name, summary and category, for the top-bar search.
    /// An empty query matches everything.
    /// </summary>
    public IReadOnlyList<ITool> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Tools;

        var q = query.Trim();
        return Tools.Where(t =>
            t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (CategoryCatalog.Find(OS, t.Category)?.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>How many tools each OS ships, for the sidebar badge tooltips.</summary>
    public static int CountFor(OSKind os) => ToolCatalog.CountFor(os);
}
