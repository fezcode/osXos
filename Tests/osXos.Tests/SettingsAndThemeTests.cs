using Avalonia.Media;
using OsXos.Models;
using OsXos.Services;
using Xunit;

namespace OsXos.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void A_missing_file_yields_the_documented_defaults()
    {
        using var dir = new TempDir();
        var settings = new SettingsService(dir.Path);

        Assert.Equal("default", settings.Theme);
        Assert.Equal("neo-grotesque", settings.Font);
        Assert.True(settings.AlwaysExplain);
        Assert.Null(settings.SidebarWidth);
    }

    [Fact]
    public void Values_round_trip_through_the_file()
    {
        using var dir = new TempDir();

        var written = new SettingsService(dir.Path);
        Assert.True(written.Update(d => d with
        {
            Theme = "harbor-night",
            Font = "mono",
            AlwaysExplain = false,
            SidebarWidth = 312.5,
        }));

        var read = new SettingsService(dir.Path);
        Assert.Equal("harbor-night", read.Theme);
        Assert.Equal("mono", read.Font);
        Assert.False(read.AlwaysExplain);
        Assert.Equal(312.5, read.SidebarWidth);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), "{ this is not json");

        var settings = new SettingsService(dir.Path);

        Assert.Equal("default", settings.Theme);
        // And the next save must repair it rather than leaving it broken.
        Assert.True(settings.Update(d => d with { Theme = "blueprint" }));
        Assert.Equal("blueprint", new SettingsService(dir.Path).Theme);
    }

    [Fact]
    public void A_partial_file_keeps_the_defaults_for_what_it_omits()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), """{"Theme":"orchid-dusk"}""");

        var settings = new SettingsService(dir.Path);

        Assert.Equal("orchid-dusk", settings.Theme);
        Assert.Equal("neo-grotesque", settings.Font);
        Assert.True(settings.AlwaysExplain);
    }

    [Fact]
    public void The_file_lands_where_the_service_says_it_does()
    {
        using var dir = new TempDir();
        var settings = new SettingsService(dir.Path);
        settings.Save();

        Assert.Equal(Path.Combine(dir.Path, "settings.json"), settings.FilePath);
        Assert.True(File.Exists(settings.FilePath));
    }
}

public class ThemeCatalogTests
{
    public static TheoryData<string> ThemeKeys()
    {
        var data = new TheoryData<string>();
        foreach (var t in ThemeCatalog.Themes) data.Add(t.Key);
        return data;
    }

    [Theory]
    [MemberData(nameof(ThemeKeys))]
    public void Every_colour_token_on_every_palette_parses(string key)
    {
        var theme = ThemeCatalog.FindTheme(key);

        // ThemeManager parses these at apply time and swallows failures, so a typo in
        // a hex value would show up as an invisible control rather than an exception.
        foreach (var hex in new[]
                 {
                     theme.Bg0, theme.BgSidebar, theme.BgTopBar, theme.BgElevated,
                     theme.Surface, theme.SurfaceHover, theme.SurfaceActive,
                     theme.Line, theme.LineBright, theme.LineSubtle,
                     theme.Text, theme.Muted, theme.Dim,
                     theme.Accent, theme.AccentHover, theme.OnAccent,
                     theme.Cyan, theme.Gold, theme.Green, theme.Red,
                 })
        {
            Assert.True(Color.TryParse(hex, out _), $"{key}: '{hex}' is not a colour");
        }

        if (theme.SidebarAccent is { } sidebarAccent)
            Assert.True(Color.TryParse(sidebarAccent, out _), $"{key}: bad SidebarAccent");
    }

    [Fact]
    public void Theme_keys_are_unique()
    {
        var keys = ThemeCatalog.Themes.Select(t => t.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Font_keys_are_unique_and_every_stack_is_non_empty()
    {
        var keys = ThemeCatalog.Fonts.Select(f => f.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ThemeCatalog.Fonts, f => Assert.False(string.IsNullOrWhiteSpace(f.FontFamilyString)));
    }

    [Fact]
    public void The_defaults_the_settings_service_hands_out_actually_exist()
    {
        // FindTheme and FindFont fall back silently, so a renamed key would leave the
        // app looking fine while quietly ignoring the saved choice.
        Assert.Equal("default", ThemeCatalog.FindTheme("default").Key);
        Assert.Equal("neo-grotesque", ThemeCatalog.FindFont("neo-grotesque").Key);
    }

    [Fact]
    public void An_unknown_key_falls_back_rather_than_throwing()
    {
        Assert.Equal(ThemeCatalog.Themes[0].Key, ThemeCatalog.FindTheme("no-such-theme").Key);
        Assert.Equal(ThemeCatalog.Fonts[0].Key, ThemeCatalog.FindFont(null).Key);
    }

    [Fact]
    public void All_nine_palettes_and_seven_font_stacks_are_present()
    {
        Assert.Equal(9, ThemeCatalog.Themes.Count);
        Assert.Equal(7, ThemeCatalog.Fonts.Count);
    }
}
