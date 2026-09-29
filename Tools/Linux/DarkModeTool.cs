namespace OsXos.Tools.Linux;

/// <summary>
/// Switches the desktop between dark and light. Linux has no single switch for this,
/// so the tool speaks to the two that cover most desktops and says which it used:
///
///   KDE Plasma  plasma-apply-colorscheme, between Breeze Light and Breeze Dark
///   GNOME &amp; co  gsettings: the color-scheme preference that GNOME 42+, libadwaita
///               apps and the desktop portal all read, plus the stock GTK theme's own
///               -dark variant so older GTK 3 apps follow along
///
/// A two-way toggle, like the other mode switches: running it again goes back.
/// </summary>
public sealed class DarkModeTool : ITool
{
    const string Schema = "org.gnome.desktop.interface";

    public static readonly ShellCommand ColorSchemeGet = new("gsettings", "get", Schema, "color-scheme");
    public static readonly ShellCommand GtkThemeGet = new("gsettings", "get", Schema, "gtk-theme");

    public static ShellCommand ColorSchemeSet(bool dark) =>
        new("gsettings", "set", Schema, "color-scheme", dark ? "prefer-dark" : "default");

    public static ShellCommand GtkThemeSet(string theme) => new("gsettings", "set", Schema, "gtk-theme", theme);

    public static ShellCommand KdeApply(string scheme) => new("plasma-apply-colorscheme", scheme);

    public const string KdeLight = "BreezeLight";
    public const string KdeDark = "BreezeDark";

    readonly IProcessRunner _runner;
    readonly Func<string?> _desktop;
    readonly Func<string?> _kdeScheme;
    readonly Func<string, bool> _gtkThemeExists;

    public DarkModeTool(IProcessRunner runner)
        : this(runner,
            () => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            ReadKdeScheme,
            GtkThemeInstalled) { }

    /// <summary>Everything the tool reads from the machine, supplied — for the tests.</summary>
    public DarkModeTool(
        IProcessRunner runner, Func<string?> desktop, Func<string?> kdeScheme, Func<string, bool> gtkThemeExists)
    {
        _runner = runner;
        _desktop = desktop;
        _kdeScheme = kdeScheme;
        _gtkThemeExists = gtkThemeExists;
    }

    public string Id => "linux.dark-mode";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.System;
    public string Name => "Switch Dark / Light Mode";
    public string Summary => "Flip the desktop between dark and light — KDE Plasma's colour scheme, or GNOME's style preference.";
    public string IconKey => "IconMoon";
    public string? Warning => null;
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Work out which desktop this is",
            "On KDE Plasma the switch is the colour scheme, changed with plasma-apply-colorscheme. Everywhere gsettings is available — GNOME, Budgie, Cinnamon, Pantheon — it is org.gnome.desktop.interface, which the desktop portal also passes on to Flatpak apps."),
        new("Read the mode it is in now",
            "On Plasma, the ColorScheme line in ~/.config/kdeglobals: any scheme with Dark in its name counts as dark. With gsettings, color-scheme — prefer-dark is dark, default or prefer-light is light — falling back to whether the GTK theme ends in -dark on desktops older than GNOME 42."),
        new("Switch to the other mode",
            "Plasma moves between Breeze Light and Breeze Dark; a custom colour scheme is replaced by the matching Breeze one, which the Review stage names. gsettings sets color-scheme, and moves the GTK theme to its -dark counterpart (or back) only when that variant is actually installed, so a hand-picked theme is never swapped for one that is missing."),
        new("Only your account is affected",
            "Both are per-user desktop settings. No sudo, no polkit, and nothing outside your own configuration changes. Run the tool again to switch back."),
    };

    // ------------------------------------------------------------------ the plan --

    sealed record Plan(bool Dark, IReadOnlyList<PreviewItem> Rows, IReadOnlyList<ShellCommand> Commands, string Desktop);

    async Task<(Plan? Plan, string? Blocker)> MakePlanAsync(CancellationToken ct)
    {
        var desktop = _desktop() ?? "";
        if (desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase) && _runner.Exists("plasma-apply-colorscheme"))
            return (KdePlan(), null);

        if (_runner.Exists("gsettings"))
            return await GnomePlanAsync(ct).ConfigureAwait(false);

        return (null,
            "Neither plasma-apply-colorscheme nor gsettings could be found, so there is no desktop setting osXos knows how to change here. " +
            "Your desktop's own appearance settings are the place to switch it.");
    }

    Plan KdePlan()
    {
        // No ColorScheme line at all is Plasma's factory state, which is Breeze Light.
        var scheme = _kdeScheme() is { Length: > 0 } s ? s : KdeLight;
        var dark = scheme.Contains("Dark", StringComparison.OrdinalIgnoreCase);
        var target = dark ? KdeLight : KdeDark;
        var apply = KdeApply(target);

        var rows = new[]
        {
            new PreviewItem("Plasma colour scheme", $"{scheme} ({Mode(dark)}) → {target} ({Mode(!dark)})"),
            new PreviewItem("Will run", apply.Display),
        };
        return new Plan(dark, rows, new[] { apply }, "KDE Plasma");
    }

    async Task<(Plan? Plan, string? Blocker)> GnomePlanAsync(CancellationToken ct)
    {
        var scheme = await _runner.RunAsync(ColorSchemeGet, ct).ConfigureAwait(false);
        var themeRead = await _runner.RunAsync(GtkThemeGet, ct).ConfigureAwait(false);

        // color-scheme arrived in GNOME 42; before that the key does not exist and the
        // GTK theme's name is the only signal there is.
        var hasScheme = scheme.Ok;
        var theme = themeRead.Ok ? Unquote(themeRead.StdOut) : "";
        var dark = hasScheme
            ? Unquote(scheme.StdOut) == "prefer-dark"
            : theme.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);
        var target = !dark;

        var rows = new List<PreviewItem>();
        var commands = new List<ShellCommand>();

        if (hasScheme)
        {
            var set = ColorSchemeSet(target);
            rows.Add(new PreviewItem("Style preference (color-scheme)",
                $"{Unquote(scheme.StdOut)} → {(target ? "prefer-dark" : "default")}"));
            commands.Add(set);
        }

        if (GtkCounterpart(theme, target, _gtkThemeExists) is { } counterpart)
        {
            rows.Add(new PreviewItem("GTK theme, for older GTK 3 apps", $"{theme} → {counterpart}"));
            commands.Add(GtkThemeSet(counterpart));
        }
        else if (theme.Length > 0)
        {
            rows.Add(new PreviewItem("GTK theme", $"{theme} — left as it is, no {(target ? "dark" : "light")} variant is installed"));
        }

        if (commands.Count == 0)
            return (null,
                $"This desktop has no color-scheme setting, and the GTK theme \"{theme}\" has no installed {(target ? "dark" : "light")} variant to switch to. " +
                "Choose a theme with a -dark counterpart in your desktop's appearance settings.");

        rows.AddRange(commands.Select(c => new PreviewItem("Will run", c.Display)));
        return (new Plan(dark, rows, commands, "GNOME settings"), null);
    }

    /// <summary>
    /// The same GTK theme in the other mode, if there is one to go to. Adwaita's dark
    /// variant is compiled into GTK 3 rather than installed as a folder, so it is the
    /// one name trusted without looking; every other variant must exist on disk.
    /// </summary>
    public static string? GtkCounterpart(string theme, bool toDark, Func<string, bool> exists)
    {
        if (theme.Length == 0) return null;
        var isDark = theme.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);
        if (isDark == toDark) return null;

        var candidate = toDark ? theme + "-dark" : theme[..^"-dark".Length];
        var builtIn = candidate is "Adwaita" or "Adwaita-dark";
        return builtIn || exists(candidate) ? candidate : null;
    }

    /// <summary>gsettings prints strings GVariant-quoted: <c>'prefer-dark'</c>.</summary>
    public static string Unquote(string value) => value.Trim().Trim('\'', '"');

    static string Mode(bool dark) => dark ? "dark" : "light";

    // ------------------------------------------------------------ inspect / run --

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var (plan, blocker) = await MakePlanAsync(ct).ConfigureAwait(false);
        if (plan is null) return ToolPreview.Blocked(blocker!);

        return new ToolPreview(plan.Rows,
            $"The desktop is in {Mode(plan.Dark)} mode. Will switch to {Mode(!plan.Dark)} mode through {plan.Desktop}.");
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var (plan, blocker) = await MakePlanAsync(ct).ConfigureAwait(false);
        if (plan is null) return ToolResult.Failure("Nothing to switch", blocker!);

        var lines = new List<string>();
        foreach (var command in plan.Commands)
        {
            var outcome = await _runner.RunAsync(command, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                lines.Add(command.Display);
                lines.Add($"exit code {outcome.ExitCode}");
                if (outcome.Message.Length > 0) lines.Add(outcome.Message);
                return ToolResult.Failure("Could not change the desktop mode", lines.ToArray());
            }
            lines.Add(command.Display);
        }

        lines.Add("Apps that follow the desktop switch on the spot; one that only checks at startup follows the next time it opens.");
        lines.Add("Run this tool again to switch back.");
        return ToolResult.Success($"The desktop is now in {Mode(!plan.Dark)} mode", lines.ToArray());
    }

    // --------------------------------------------------------- reading the machine --

    /// <summary>The <c>ColorScheme</c> in the <c>[General]</c> group of kdeglobals, or null.</summary>
    static string? ReadKdeScheme()
    {
        try
        {
            var path = Path.Combine(XdgPaths.ConfigHome, "kdeglobals");
            return File.Exists(path) ? ParseKdeScheme(File.ReadAllLines(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? ParseKdeScheme(IEnumerable<string> lines)
    {
        var inGeneral = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inGeneral = line == "[General]";
                continue;
            }
            if (inGeneral && line.StartsWith("ColorScheme=", StringComparison.Ordinal))
                return line["ColorScheme=".Length..].Trim();
        }
        return null;
    }

    /// <summary>Whether a GTK theme of this name is installed anywhere GTK looks.</summary>
    static bool GtkThemeInstalled(string name)
    {
        string[] roots =
        {
            Path.Combine(XdgPaths.DataHome, "themes"),
            Path.Combine(XdgPaths.Home, ".themes"),
            "/usr/share/themes",
            "/usr/local/share/themes",
        };
        return roots.Any(r => Directory.Exists(Path.Combine(r, name)));
    }
}
