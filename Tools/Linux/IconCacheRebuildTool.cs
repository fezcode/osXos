namespace OsXos.Tools.Linux;

/// <summary>
/// Rebuilds the GTK icon theme caches under the user's own icon directory. The Linux
/// answer to "my icons are wrong": a theme whose icon-theme.cache is stale keeps
/// serving the old icons even after the files beneath it have changed.
/// </summary>
public sealed class IconCacheRebuildTool : ITool
{
    public const string Binary = "gtk-update-icon-cache";

    /// <summary>
    /// -f forces a rebuild even when the cache looks current, -t skips the check that
    /// the directory is a valid icon theme, which some partially-installed themes fail.
    /// </summary>
    public static ShellCommand CommandFor(string themeDir) => new(Binary, "-f", "-t", themeDir);

    readonly string _iconsDir;
    readonly IProcessRunner _runner;

    public IconCacheRebuildTool(IProcessRunner runner)
        : this(Path.Combine(XdgPaths.DataHome, "icons"), runner) { }

    public IconCacheRebuildTool(string iconsDir, IProcessRunner runner)
    {
        _iconsDir = iconsDir;
        _runner = runner;
    }

    public string Id => "linux.icon-cache-rebuild";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Rebuild Icon Cache";
    public string Summary => "Regenerate the GTK icon theme caches under your home directory.";
    public string IconKey => "IconImage";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Find your icon themes",
            "Each theme directory under $XDG_DATA_HOME/icons — normally ~/.local/share/icons — is a theme you installed for yourself. System themes in /usr/share/icons need root to rebuild and are deliberately left alone."),
        new("Why the cache goes stale",
            "GTK reads icon-theme.cache instead of walking the theme directory on every lookup, which is what keeps application menus fast. Install or edit a theme by hand and that cache still describes the old contents, so the old icons keep being drawn."),
        new("Run gtk-update-icon-cache on each",
            "-f forces a rebuild even when the cache looks current, and -t skips the validity check that partially-installed themes tend to fail. One invocation per theme directory; each is listed on the Review stage before anything runs."),
        new("Nothing is deleted",
            "Only the index is regenerated — no icon file is added, changed or removed. Applications already running may need restarting before they pick up the new icons."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists(Binary))
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"{Binary} is not installed. It ships with the GTK development tools — on Debian and Ubuntu that is the gtk-update-icon-cache package, on Fedora gtk3-devel-tools, on Arch gtk-update-icon-cache."));
        }

        if (!Directory.Exists(_iconsDir))
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"No user icon themes found — {_iconsDir} does not exist. Themes installed system-wide are rebuilt by your package manager, not here."));
        }

        string[] themes;
        try { themes = Directory.GetDirectories(_iconsDir); }
        catch (Exception ex) { return Task.FromResult(ToolPreview.Blocked($"Could not read {_iconsDir}: {ex.Message}")); }

        if (themes.Length == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"No user icon themes found — {_iconsDir} is empty."));
        }

        var items = themes
            .OrderBy(t => t, StringComparer.Ordinal)
            .Select(t => new PreviewItem(Path.GetFileName(t), CommandFor(t).Display))
            .ToList();

        return Task.FromResult(new ToolPreview(items,
            $"{items.Count} icon theme{(items.Count == 1 ? "" : "s")} to rebuild in {_iconsDir}"));
    }

    public async Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct)
    {
        var lines = new List<string>();
        int ok = 0, failed = 0;

        foreach (var item in preview.Items)
        {
            var themeDir = Path.Combine(_iconsDir, item.Label);
            var outcome = await _runner.RunAsync(CommandFor(themeDir), ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                ok++;
                lines.Add($"{item.Label} — rebuilt.");
            }
            else
            {
                failed++;
                lines.Add($"{item.Label} — failed: {outcome.Message}");
            }
        }

        if (ok == 0) return ToolResult.Failure("No icon theme could be rebuilt", lines.ToArray());

        lines.Add("Restart any running application that still shows an old icon.");
        return failed == 0
            ? ToolResult.Success($"Rebuilt {ok} icon theme{(ok == 1 ? "" : "s")}", lines.ToArray())
            : ToolResult.Success($"Rebuilt {ok} of {ok + failed} icon themes", lines.ToArray());
    }
}
