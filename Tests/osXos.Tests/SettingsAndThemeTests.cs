using Avalonia.Media;
using OsXos.Models;
using OsXos.Menus;
using OsXos.Services;
using OsXos.ViewModels;
using Xunit;

namespace OsXos.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void A_missing_file_yields_the_documented_defaults()
    {
        using var dir = new TempDir();
        var settings = new SettingsService(dir.Path);

        Assert.Equal("default", settings.LightTheme);
        Assert.Equal("harbor-night", settings.DarkTheme);
        Assert.False(settings.DarkMode);
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
            LightTheme = "blueprint",
            DarkTheme = "lantern-oak",
            DarkMode = true,
            Font = "mono",
            AlwaysExplain = false,
            SidebarWidth = 312.5,
        }));

        var read = new SettingsService(dir.Path);
        Assert.Equal("blueprint", read.LightTheme);
        Assert.Equal("lantern-oak", read.DarkTheme);
        Assert.True(read.DarkMode);
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

        Assert.Equal("default", settings.LightTheme);
        // And the next save must repair it rather than leaving it broken.
        Assert.True(settings.Update(d => d with { LightTheme = "blueprint" }));
        Assert.Equal("blueprint", new SettingsService(dir.Path).LightTheme);
    }

    [Fact]
    public void A_partial_file_keeps_the_defaults_for_what_it_omits()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), """{"LightTheme":"orchid-dusk"}""");

        var settings = new SettingsService(dir.Path);

        Assert.Equal("orchid-dusk", settings.LightTheme);
        Assert.Equal("harbor-night", settings.DarkTheme);
        Assert.Equal("neo-grotesque", settings.Font);
        Assert.True(settings.AlwaysExplain);
    }

    [Fact]
    public void A_light_palette_saved_before_dark_mode_existed_becomes_the_light_choice()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), """{"Theme":"orchid-dusk"}""");

        var settings = new SettingsService(dir.Path);

        Assert.Equal("orchid-dusk", settings.LightTheme);
        Assert.Equal("harbor-night", settings.DarkTheme);
        Assert.False(settings.DarkMode);
    }

    [Fact]
    public void A_dark_palette_saved_before_dark_mode_existed_opens_in_dark_mode()
    {
        // Someone who had picked Lantern Oak must not upgrade into a light window.
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), """{"Theme":"lantern-oak"}""");

        var settings = new SettingsService(dir.Path);

        Assert.Equal("lantern-oak", settings.DarkTheme);
        Assert.Equal("default", settings.LightTheme);
        Assert.True(settings.DarkMode);
    }

    [Fact]
    public void The_legacy_key_is_not_written_back()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(file, """{"Theme":"blueprint"}""");

        new SettingsService(dir.Path).Save();

        Assert.DoesNotContain("\"Theme\"", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal("blueprint", new SettingsService(dir.Path).LightTheme);
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
    public void Both_modes_have_palettes_and_every_palette_belongs_to_exactly_one()
    {
        Assert.NotEmpty(ThemeCatalog.LightThemes);
        Assert.NotEmpty(ThemeCatalog.DarkThemes);
        Assert.All(ThemeCatalog.LightThemes, t => Assert.False(t.IsDark));
        Assert.All(ThemeCatalog.DarkThemes, t => Assert.True(t.IsDark));
        Assert.Equal(ThemeCatalog.Themes.Count, ThemeCatalog.LightThemes.Count + ThemeCatalog.DarkThemes.Count);
    }

    [Fact]
    public void The_per_mode_defaults_exist_in_their_own_mode()
    {
        Assert.Equal("default", ThemeCatalog.FindTheme("default", dark: false).Key);
        Assert.Equal("harbor-night", ThemeCatalog.FindTheme("harbor-night", dark: true).Key);
    }

    [Fact]
    public void A_palette_named_for_the_wrong_mode_falls_back_to_that_modes_first()
    {
        // A hand-edited file with a dark palette as the light choice must not leave
        // light mode rendering dark.
        Assert.Equal(ThemeCatalog.LightThemes[0].Key, ThemeCatalog.FindTheme("harbor-night", dark: false).Key);
        Assert.Equal(ThemeCatalog.DarkThemes[0].Key, ThemeCatalog.FindTheme("blueprint", dark: true).Key);
        Assert.Equal(ThemeCatalog.DarkThemes[0].Key, ThemeCatalog.FindTheme(null, dark: true).Key);
    }

    [Fact]
    public void All_nine_palettes_and_seven_font_stacks_are_present()
    {
        Assert.Equal(9, ThemeCatalog.Themes.Count);
        Assert.Equal(7, ThemeCatalog.Fonts.Count);
    }
}

/// <summary>
/// The light/dark switch as the app drives it, through the real settings view model
/// wired to a temp data directory rather than the user's own settings.
/// </summary>
public class ThemeModeTests
{
    static SettingsViewModel Vm(TempDir dir) => new(new AppServices(dir.Path));

    [Fact]
    public void Toggling_flips_the_mode_and_remembers_it_without_a_save()
    {
        using var dir = new TempDir();
        var vm = Vm(dir);
        Assert.False(vm.IsDarkMode);

        vm.ToggleThemeModeCommand.Execute().Subscribe();

        Assert.True(vm.IsDarkMode);
        Assert.False(vm.IsDirty);
        Assert.True(new SettingsService(dir.Path).DarkMode);
    }

    [Fact]
    public void Each_mode_shows_only_its_own_palettes_and_its_own_choice()
    {
        using var dir = new TempDir();
        var vm = Vm(dir);

        Assert.All(vm.Themes, t => Assert.False(t.IsDark));
        Assert.Equal("default", vm.SelectedTheme.Key);

        vm.IsDarkMode = true;

        Assert.All(vm.Themes, t => Assert.True(t.IsDark));
        Assert.Equal("harbor-night", vm.SelectedTheme.Key);
    }

    [Fact]
    public void A_palette_picked_in_one_mode_survives_a_round_trip_through_the_other()
    {
        using var dir = new TempDir();
        var vm = Vm(dir);

        vm.SelectedTheme = ThemeCatalog.FindTheme("blueprint");
        vm.IsDarkMode = true;
        vm.SelectedTheme = ThemeCatalog.FindTheme("lantern-oak");
        vm.IsDarkMode = false;

        Assert.Equal("blueprint", vm.SelectedTheme.Key);
        vm.IsDarkMode = true;
        Assert.Equal("lantern-oak", vm.SelectedTheme.Key);
    }

    [Fact]
    public void The_list_pushing_null_or_the_other_modes_palette_back_is_ignored()
    {
        // What a ListBox does while its ItemsSource is being swapped under it.
        using var dir = new TempDir();
        var vm = Vm(dir);

        vm.SelectedTheme = null!;
        vm.SelectedTheme = ThemeCatalog.FindTheme("harbor-night");

        Assert.Equal("default", vm.SelectedTheme.Key);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Palettes_still_wait_for_save_and_discard_puts_both_back()
    {
        using var dir = new TempDir();
        var vm = Vm(dir);

        vm.SelectedTheme = ThemeCatalog.FindTheme("blueprint");
        vm.IsDarkMode = true;
        vm.SelectedTheme = ThemeCatalog.FindTheme("lantern-oak");
        Assert.True(vm.IsDirty);
        Assert.Equal("default", new SettingsService(dir.Path).LightTheme);

        vm.Revert();

        Assert.False(vm.IsDirty);
        Assert.Equal("harbor-night", vm.SelectedTheme.Key);
        Assert.True(vm.IsDarkMode);

        vm.SelectedTheme = ThemeCatalog.FindTheme("lantern-oak");
        vm.Save();

        var saved = new SettingsService(dir.Path);
        Assert.Equal("lantern-oak", saved.DarkTheme);
        Assert.Equal("default", saved.LightTheme);
    }

    [Fact]
    public void The_view_menu_carries_a_dark_mode_row_that_follows_the_mode()
    {
        using var dir = new TempDir();
        var services = new AppServices(dir.Path);
        var main = new MainWindowViewModel(services);
        var model = new AppMenuModel(main, services.Tools, services.OS);

        AppMenuNode Row() => model.Build().Single(m => m.Id == "view").Items!.Single(i => i.Id == "view.darkmode");

        Assert.Equal(false, Row().Check);
        model.Invoke("view.darkmode");
        Assert.True(main.Settings.IsDarkMode);
        Assert.Equal(true, Row().Check);
    }
}
