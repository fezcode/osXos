using OsXos.Menus;
using OsXos.Tools;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The menu tree is built from the tool catalogue and consumed by two different
/// bridges, so the things worth pinning down are its shape and the promise that every
/// row it produces is answered. A dead menu row is not noticed until a user clicks it.
/// </summary>
public class MenuTests
{
    static ToolRegistry Registry(OSKind os) => new(os, os switch
    {
        OSKind.Windows => TestCatalog.Windows(),
        OSKind.MacOS => ToolCatalog.MacOS(new FakeRunner()),
        _ => ToolCatalog.Linux(new FakeRunner()),
    });

    // The real builder, not a replica of it. An earlier version of this test rebuilt
    // the tree by hand and so could not have caught the menubar showing the app name
    // twice, because the hand-written copy did not have the bug.
    static IReadOnlyList<AppMenuNode> Tree(OSKind os, string? page = null, bool hasQuery = false)
    {
        var registry = Registry(os);
        var pages = registry.Categories.Select(c => c.Name).Append("Settings").ToList();
        return AppMenuModel.BuildTree(os, registry, pages, page ?? pages[0], hasQuery);
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Linux)]
    public void The_tools_menu_reaches_every_tool_on_the_platform(OSKind os)
    {
        var ids = Tree(os).SelectMany(n => n.Ids()).ToList();

        foreach (var tool in Registry(os).Tools)
            Assert.Contains($"tool.{tool.Id}", ids);
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.Linux)]
    public void The_first_menu_is_File_where_the_host_already_shows_the_app_name(OSKind os)
    {
        // Hisashi draws "osXos" beside the menus itself, and a Linux panel does too,
        // so naming our first menu after the app produced a bar reading
        // "osXos osXos Tools View Help".
        var first = Tree(os)[0];
        Assert.Equal("File", first.Label);
        Assert.DoesNotContain(Tree(os), m => m.Label == "osXos");
    }

    [Fact]
    public void On_macOS_the_first_menu_is_the_application_menu()
    {
        // The opposite convention: macOS turns the first submenu into the app menu,
        // which is named after the app and owns About, Settings and Quit.
        var first = Tree(OSKind.MacOS)[0];
        Assert.Equal("osXos", first.Label);
        Assert.Contains(first.Items!, i => i.Id == "app.about");
        Assert.Contains(first.Items!, i => i.Id == "app.quit");
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Linux)]
    public void About_is_always_reachable(OSKind os)
    {
        // It moves out of the first menu off macOS, so Help has to carry it.
        Assert.Contains("help.about", Tree(os).SelectMany(n => n.Ids()));
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Linux)]
    public void No_two_top_level_menus_share_a_name(OSKind os)
    {
        var labels = Tree(os).Select(m => m.Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Clear_Search_is_enabled_only_when_there_is_a_search_to_clear()
    {
        AppMenuNode Find(bool hasQuery) => Tree(OSKind.Windows, hasQuery: hasQuery)
            .Single(m => m.Id == "view").Items!.Single(i => i.Id == "view.clearsearch");

        Assert.False(Find(false).Enabled);
        Assert.True(Find(true).Enabled);
    }

    [Fact]
    public void The_current_page_is_the_only_checked_View_row()
    {
        var pages = Registry(OSKind.Windows).Categories.Select(c => c.Name).Append("Settings").ToList();
        var view = Tree(OSKind.Windows, page: pages[1]).Single(m => m.Id == "view");

        var checkedRows = view.Items!.Where(i => i.Check == true).Select(i => i.Label).ToList();
        Assert.Equal(new[] { pages[1] }, checkedRows);
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Linux)]
    public void The_shortcut_hints_suit_the_platform(OSKind os)
    {
        var app = Tree(os).Single(n => n.Id == "app");
        var quit = app.Items!.Single(i => i.Id == "app.quit");

        // Alt+F4 does not exist on macOS and Cmd does not exist on Windows. A hint
        // that lies is worse than no hint.
        Assert.Equal(os == OSKind.MacOS ? "Cmd+Q" : "Alt+F4", quit.Shortcut);
    }

    [Fact]
    public void Ids_walks_leaves_only_and_skips_separators_and_headers()
    {
        var menu = AppMenuNode.Menu("root", "Root",
            AppMenuNode.Item("a", "A"),
            AppMenuNode.Separator(),
            AppMenuNode.Menu("sub", "Sub",
                AppMenuNode.Item("b", "B"),
                AppMenuNode.Item("c", "C")));

        Assert.Equal(new[] { "a", "b", "c" }, menu.Ids());
    }

    [Fact]
    public void Every_static_id_in_the_tree_is_one_the_model_answers()
    {
        // AppMenuModel.Invoke handles the list-derived ids by prefix; these are the
        // ones spelled out in a switch, and this is what catches a renamed case.
        var ids = Tree(OSKind.Windows)
            .SelectMany(n => n.Ids())
            .Where(id => !id.StartsWith("tool.", StringComparison.Ordinal)
                      && !id.StartsWith("view.", StringComparison.Ordinal))
            .ToList();

        Assert.All(ids, id => Assert.Contains(id, AppMenuModel.StaticIds));
    }
}

public class HoswlTranslationTests
{
    [Fact]
    public void A_separator_becomes_a_sep_row()
    {
        var node = HisashiMenuBridge.ToHoswl(AppMenuNode.Separator());
        Assert.True(node.Sep);
        Assert.Null(node.Id);
    }

    [Fact]
    public void A_submenu_carries_its_children_and_no_shortcut()
    {
        var node = HisashiMenuBridge.ToHoswl(
            AppMenuNode.Menu("view", "View", AppMenuNode.Item("view.a", "A")));

        Assert.Equal("view", node.Id);
        Assert.Equal("View", node.Label);
        Assert.NotNull(node.Items);
        Assert.Single(node.Items!);
        Assert.Equal("view.a", node.Items![0].Id);
    }

    [Fact]
    public void Enabled_is_sent_only_when_it_is_false()
    {
        // The protocol treats a missing "enabled" as true, so sending it always
        // would just be noise on the wire.
        Assert.Null(HisashiMenuBridge.ToHoswl(AppMenuNode.Item("a", "A")).Enabled);
        Assert.False(HisashiMenuBridge.ToHoswl(AppMenuNode.Item("a", "A", enabled: false)).Enabled);
    }

    [Fact]
    public void A_checkmark_and_a_shortcut_survive_the_translation()
    {
        var node = HisashiMenuBridge.ToHoswl(
            AppMenuNode.Item("app.settings", "Settings", "Ctrl+,", check: true));

        Assert.True(node.Check);
        Assert.Equal("Ctrl+,", node.Key);
    }
}

public class NativeGestureTests
{
    [Theory]
    [InlineData("Cmd+Q")]
    [InlineData("Cmd+,")]
    [InlineData("Ctrl+,")]
    [InlineData("Ctrl+N")]
    public void The_shortcut_hints_osXos_actually_uses_all_parse(string shortcut)
    {
        // A hint that will not parse silently loses its key binding on macOS, so the
        // exact strings the menu ships are pinned here.
        Assert.NotNull(NativeMenuBridge.TryGesture(shortcut));
    }

    [Fact]
    public void Alt_F4_parses_even_though_only_Windows_uses_it()
    {
        Assert.NotNull(NativeMenuBridge.TryGesture("Alt+F4"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+NotAKey")]
    public void An_unusable_hint_is_dropped_rather_than_guessed_at(string? shortcut)
    {
        Assert.Null(NativeMenuBridge.TryGesture(shortcut));
    }
}
