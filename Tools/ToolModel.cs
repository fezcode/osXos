namespace OsXos.Tools;

/// <summary>The three operating systems osXos ships for.</summary>
public enum OSKind
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>
/// The category spine. Every OS draws from this one enum, but each names and orders
/// its own subset — "Shell" is Explorer on Windows, Finder on macOS and the desktop
/// environment on Linux — so the display name lives in <see cref="CategoryCatalog"/>
/// rather than on the value.
/// </summary>
public enum ToolCategory
{
    Maintenance,
    Shell,
    System,
    Network,
    Privacy,
    Developer,
    Packages,
    Services,
    AI,
}

/// <summary>One numbered step on a tool window's Explain stage.</summary>
public sealed record ToolStep(string Title, string Detail);

/// <summary>
/// One thing a tool found and would act on: a file to delete, a command to run, a
/// setting to change. <paramref name="Bytes"/> is set only where a size is meaningful,
/// and drives the reclaimable total.
/// </summary>
public sealed record PreviewItem(string Label, string? Detail = null, long? Bytes = null);

/// <summary>
/// The read-only result of <see cref="ITool.InspectAsync"/> — everything the Review
/// stage shows before anything is touched.
/// </summary>
public sealed record ToolPreview(
    IReadOnlyList<PreviewItem> Items,
    string Summary,
    string? Blocker = null)
{
    /// <summary>
    /// Set when the inspection found that this particular run needs administrator
    /// rights, even though the tool does not always. A cache folder owned by another
    /// user is the shape of this: the tool is ordinary, today's work is not.
    /// </summary>
    public bool NeedsElevation { get; init; }

    /// <summary>
    /// False when the tool cannot proceed on this machine — systemd-resolved absent,
    /// a setting already in the desired state, nothing found to clean. The Review
    /// stage disables Run and shows <see cref="Blocker"/> instead of pretending.
    /// </summary>
    public bool CanRun => Blocker is null;

    public long TotalBytes => Items.Sum(i => i.Bytes ?? 0);

    public static ToolPreview Blocked(string reason) => new(Array.Empty<PreviewItem>(), reason, reason);
}

/// <summary>
/// How far a run has got. <paramref name="Total"/> is 0 when the work cannot be
/// counted — a shell command either finishes or does not — and the UI shows an
/// indeterminate bar for that rather than inventing a percentage.
/// </summary>
public readonly record struct ToolProgress(int Done, int Total, string? Item = null)
{
    public bool IsCountable => Total > 0;
    public double Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <summary>What actually happened, shown on the Result stage.</summary>
public sealed record ToolResult(bool Ok, string Headline, IReadOnlyList<string> Lines)
{
    public static ToolResult Success(string headline, params string[] lines) => new(true, headline, lines);
    public static ToolResult Failure(string headline, params string[] lines) => new(false, headline, lines);
}

/// <summary>
/// A single utility. Two phases, always in this order: <see cref="InspectAsync"/> looks
/// and never mutates, then <see cref="RunAsync"/> acts — and only on a preview the user
/// has already been shown.
/// </summary>
public interface ITool
{
    /// <summary>Stable identifier, "<c>windows.icon-cache</c>". Used for settings keys.</summary>
    string Id { get; }

    OSKind Platform { get; }
    ToolCategory Category { get; }

    string Name { get; }

    /// <summary>One line, shown on the card in the category list.</summary>
    string Summary { get; }

    /// <summary>Resource key of the tool's mark, resolved against App.axaml.</summary>
    string IconKey { get; }

    /// <summary>
    /// Shown as a gold banner on the Review stage. Null for a tool with nothing to warn
    /// about; set for anything that restarts the shell or cannot be undone.
    /// </summary>
    string? Warning { get; }

    /// <summary>Whether Run is styled as a destructive action.</summary>
    bool IsDestructive { get; }

    /// <summary>
    /// Whether this tool cannot do its job inside the user's own account. A default
    /// member rather than a required one: every tool shipped so far works unelevated,
    /// and that stays the norm - a tool overriding this is making a claim it should
    /// have to write down.
    /// </summary>
    bool RequiresElevation => false;

    /// <summary>The Explain stage, in order.</summary>
    IReadOnlyList<ToolStep> Steps { get; }

    Task<ToolPreview> InspectAsync(CancellationToken ct);

    /// <summary>
    /// Acts on a preview the user has seen. <paramref name="progress"/> is optional:
    /// a tool whose work is one fast command simply never reports, and the UI shows
    /// an indeterminate bar. Anything that loops over thousands of files must report,
    /// or the window looks frozen for as long as it runs.
    /// </summary>
    Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null);
}
