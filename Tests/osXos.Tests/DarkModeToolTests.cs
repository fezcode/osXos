using OsXos.Tools;
using Xunit;
using LinuxDarkMode = OsXos.Tools.Linux.DarkModeTool;
using MacDarkMode = OsXos.Tools.MacOS.DarkModeTool;
using WindowsDarkMode = OsXos.Tools.Windows.DarkModeTool;

namespace OsXos.Tests;

/// <summary>
/// Switch Dark / Light Mode on all three platforms. Windows runs against in-memory
/// registry values and macOS and Linux against a scripted runner, so none of these
/// changes the appearance of the machine running the tests.
/// </summary>
public class DarkModeToolTests
{
    // ------------------------------------------------------------------ Windows --

    [Theory]
    [InlineData(1, 1, 0)] // light → dark
    [InlineData(0, 0, 1)] // dark → light
    [InlineData(0, 1, 0)] // Custom (dark apps, light taskbar) → fully dark
    [InlineData(1, 0, 0)] // Custom the other way → fully dark
    public async Task Windows_moves_both_halves_to_the_same_mode(int apps, int system, int expected)
    {
        var appearance = new FakeWindowsAppearance { AppsUseLightTheme = apps, SystemUsesLightTheme = system };
        var tool = new WindowsDarkMode(appearance);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal(expected, appearance.AppsUseLightTheme);
        Assert.Equal(expected, appearance.SystemUsesLightTheme);
        Assert.Equal(1, appearance.BroadcastCount);
    }

    [Fact]
    public async Task Windows_run_twice_is_back_where_it_started()
    {
        var appearance = new FakeWindowsAppearance();
        var tool = new WindowsDarkMode(appearance);

        await tool.RunAsync(await tool.InspectAsync(default), default);
        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Equal(1, appearance.AppsUseLightTheme);
        Assert.Equal(1, appearance.SystemUsesLightTheme);
    }

    [Fact]
    public async Task Windows_inspect_names_both_values_and_changes_nothing()
    {
        var appearance = new FakeWindowsAppearance { AppsUseLightTheme = 0, SystemUsesLightTheme = 1 };
        var preview = await new WindowsDarkMode(appearance).InspectAsync(default);

        Assert.Contains("Custom mode", preview.Summary, StringComparison.Ordinal);
        Assert.Contains(preview.Items, i => i.Detail!.Contains("AppsUseLightTheme = 0 → 0", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Detail!.Contains("SystemUsesLightTheme = 1 → 0", StringComparison.Ordinal));
        Assert.Equal(0, appearance.AppsUseLightTheme);
        Assert.Equal(1, appearance.SystemUsesLightTheme);
        Assert.Equal(0, appearance.BroadcastCount);
    }

    // -------------------------------------------------------------------- macOS --

    [Fact]
    public async Task Mac_treats_a_missing_interface_style_as_light_and_goes_dark()
    {
        // defaults read exits non-zero in light mode: the key only exists when dark.
        var runner = new FakeRunner().WithBinary("osascript")
            .Returns(MacDarkMode.ReadCommand.Display, 1, stderr: "does not exist");
        var tool = new MacDarkMode(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Contains(MacDarkMode.WriteCommand(true).Display, runner.RanDisplays);
    }

    [Fact]
    public async Task Mac_goes_light_from_dark()
    {
        var runner = new FakeRunner().WithBinary("osascript")
            .Returns(MacDarkMode.ReadCommand.Display, 0, stdout: "Dark\n");
        var tool = new MacDarkMode(runner);

        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Contains(MacDarkMode.WriteCommand(false).Display, runner.RanDisplays);
    }

    [Fact]
    public void Mac_asks_system_events_for_the_documented_appearance_property()
    {
        Assert.Equal(
            "osascript -e \"tell application \"System Events\" to tell appearance preferences to set dark mode to true\"",
            MacDarkMode.WriteCommand(true).Display);
        Assert.Equal("defaults read -g AppleInterfaceStyle", MacDarkMode.ReadCommand.Display);
    }

    [Fact]
    public async Task Mac_says_where_to_allow_it_when_automation_was_declined()
    {
        var runner = new FakeRunner().WithBinary("osascript")
            .Returns(MacDarkMode.ReadCommand.Display, 1)
            .Returns(MacDarkMode.WriteCommand(true).Display, 1,
                stderr: "execution error: Not authorized to send Apple events to System Events. (-1743)");
        var tool = new MacDarkMode(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains("Automation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mac_blocks_without_osascript()
    {
        var preview = await new MacDarkMode(new FakeRunner()).InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    // -------------------------------------------------------------------- Linux --

    static LinuxDarkMode Linux(FakeRunner runner, string? desktop = "GNOME", string? kdeScheme = null,
        params string[] installedThemes) =>
        new(runner, () => desktop, () => kdeScheme, name => installedThemes.Contains(name));

    [Fact]
    public async Task Gnome_flips_color_scheme_and_the_stock_adwaita_theme_with_it()
    {
        var runner = new FakeRunner().WithBinary("gsettings")
            .Returns(LinuxDarkMode.ColorSchemeGet.Display, 0, "'default'\n")
            .Returns(LinuxDarkMode.GtkThemeGet.Display, 0, "'Adwaita'\n");
        var tool = Linux(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Contains("gsettings set org.gnome.desktop.interface color-scheme prefer-dark", runner.RanDisplays);
        Assert.Contains("gsettings set org.gnome.desktop.interface gtk-theme Adwaita-dark", runner.RanDisplays);
    }

    [Fact]
    public async Task Gnome_goes_back_to_default_from_prefer_dark()
    {
        var runner = new FakeRunner().WithBinary("gsettings")
            .Returns(LinuxDarkMode.ColorSchemeGet.Display, 0, "'prefer-dark'\n")
            .Returns(LinuxDarkMode.GtkThemeGet.Display, 0, "'Adwaita-dark'\n");
        var tool = Linux(runner);

        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Contains("gsettings set org.gnome.desktop.interface color-scheme default", runner.RanDisplays);
        Assert.Contains("gsettings set org.gnome.desktop.interface gtk-theme Adwaita", runner.RanDisplays);
    }

    [Fact]
    public async Task Gnome_leaves_a_theme_alone_when_its_other_variant_is_not_installed()
    {
        // A hand-picked theme must never be swapped for one that does not exist —
        // GTK would fall back to something the user did not choose.
        var runner = new FakeRunner().WithBinary("gsettings")
            .Returns(LinuxDarkMode.ColorSchemeGet.Display, 0, "'default'\n")
            .Returns(LinuxDarkMode.GtkThemeGet.Display, 0, "'Orchis'\n");
        var tool = Linux(runner);

        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Contains("gsettings set org.gnome.desktop.interface color-scheme prefer-dark", runner.RanDisplays);
        Assert.DoesNotContain(runner.RanDisplays, d => d.Contains("gtk-theme Orchis", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gnome_uses_an_installed_dark_variant()
    {
        var runner = new FakeRunner().WithBinary("gsettings")
            .Returns(LinuxDarkMode.ColorSchemeGet.Display, 0, "'default'\n")
            .Returns(LinuxDarkMode.GtkThemeGet.Display, 0, "'Yaru'\n");
        var tool = Linux(runner, installedThemes: "Yaru-dark");

        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Contains("gsettings set org.gnome.desktop.interface gtk-theme Yaru-dark", runner.RanDisplays);
    }

    [Fact]
    public async Task Gnome_before_42_switches_on_the_theme_name_alone()
    {
        var runner = new FakeRunner().WithBinary("gsettings")
            .Returns(LinuxDarkMode.ColorSchemeGet.Display, 1, stderr: "No such key \"color-scheme\"")
            .Returns(LinuxDarkMode.GtkThemeGet.Display, 0, "'Adwaita-dark'\n");
        var tool = Linux(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Contains("gsettings set org.gnome.desktop.interface gtk-theme Adwaita", runner.RanDisplays);
        Assert.DoesNotContain(runner.RanDisplays, d => d.Contains("color-scheme", StringComparison.Ordinal) && d.Contains(" set ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gnome_before_42_with_no_variant_blocks_rather_than_doing_nothing()
    {
        var runner = new FakeRunner().WithBinary("gsettings")
            .Returns(LinuxDarkMode.ColorSchemeGet.Display, 1)
            .Returns(LinuxDarkMode.GtkThemeGet.Display, 0, "'Orchis'\n");

        var preview = await Linux(runner).InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.Contains("Orchis", preview.Blocker, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BreezeLight", "plasma-apply-colorscheme BreezeDark")]
    [InlineData("BreezeDark", "plasma-apply-colorscheme BreezeLight")]
    [InlineData("MyCustomDark", "plasma-apply-colorscheme BreezeLight")]
    [InlineData(null, "plasma-apply-colorscheme BreezeDark")] // unset is Plasma's default, Breeze Light
    public async Task Kde_moves_between_the_breeze_schemes(string? current, string expected)
    {
        var runner = new FakeRunner().WithBinary("plasma-apply-colorscheme", "gsettings");
        var tool = Linux(runner, desktop: "KDE", kdeScheme: current);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal(new[] { expected }, runner.RanDisplays.Where(d => !d.StartsWith("gsettings get", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Linux_blocks_when_neither_desktop_tool_is_there()
    {
        var preview = await Linux(new FakeRunner(), desktop: "XFCE").InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    [Fact]
    public async Task Linux_reports_the_failing_command()
    {
        var runner = new FakeRunner().WithBinary("gsettings")
            .Returns(LinuxDarkMode.ColorSchemeGet.Display, 0, "'default'\n")
            .Returns(LinuxDarkMode.GtkThemeGet.Display, 0, "'Orchis'\n")
            .Returns(LinuxDarkMode.ColorSchemeSet(true).Display, 1, stderr: "dconf: permission denied");
        var tool = Linux(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains("permission denied", StringComparison.Ordinal));
    }

    [Fact]
    public void Kdeglobals_is_read_from_the_general_group_only()
    {
        var lines = new[]
        {
            "[Colors:View]", "ColorScheme=NotThisOne",
            "[General]", "Name=Breeze", "ColorScheme=BreezeDark",
            "[KDE]", "ColorScheme=NorThisOne",
        };
        Assert.Equal("BreezeDark", LinuxDarkMode.ParseKdeScheme(lines));
        Assert.Null(LinuxDarkMode.ParseKdeScheme(new[] { "[General]", "Name=x" }));
    }

    // -------------------------------------------------------------- all three --

    [Fact]
    public void Every_platform_ships_it_in_system_without_elevation()
    {
        var tools = new ITool[]
        {
            new WindowsDarkMode(new FakeWindowsAppearance()),
            new MacDarkMode(new FakeRunner()),
            new LinuxDarkMode(new FakeRunner()),
        };

        Assert.All(tools, t =>
        {
            Assert.Equal(ToolCategory.System, t.Category);
            Assert.False(t.IsDestructive);
            Assert.False(t.RequiresElevation);
            Assert.True(t.Steps.Count >= 4);
        });
        Assert.Contains(TestCatalog.Windows(), t => t.Id == "windows.dark-mode");
        Assert.Contains(ToolCatalog.MacOS(new FakeRunner()), t => t.Id == "macos.dark-mode");
        Assert.Contains(ToolCatalog.Linux(new FakeRunner()), t => t.Id == "linux.dark-mode");
    }
}
