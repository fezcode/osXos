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
    static IReadOnlyList<AppMenuNode> Tree(OSKind os)
    {
        // AppMenuModel needs a view model, which needs Avalonia. The tree's shape is
        // what matters here, so it is rebuilt from the same pieces the model uses.
        var registry = new ToolRegistry(os, os switch
        {
            OSKind.Windows => ToolCatalog.Windows(new FakeRunner(), new FakeExplorerSettings()),
            OSKind.MacOS => ToolCatalog.MacOS(new FakeRunner()),
            _ => ToolCatalog.Linux(new FakeRunner()),
        });

        var quitKey = os == OSKind.MacOS ? "Cmd+Q" : "Alt+F4";
        var prefsKey = os == OSKind.MacOS ? "Cmd+," : "Ctrl+,";

        return new List<AppMenuNode>
        {
            AppMenuNode.Menu("app", "osXos",
                AppMenuNode.Item("app.about", "About osXos"),
                AppMenuNode.Separator(),
                AppMenuNode.Item("app.settings", "Settings", prefsKey),
                AppMenuNode.Item("app.data", "Open Settings Folder"),
                AppMenuNode.Separator(),
                AppMenuNode.Item("app.quit", "Quit osXos", quitKey)),

            new("tools", "Tools", Items: registry.Categories
                .Select(c => AppMenuNode.Menu($"cat.{c.Category}", c.Name,
                    registry.InCategory(c.Category)
                        .Select(t => AppMenuNode.Item($"tool.{t.Id}", t.Name + "..."))
                        .ToArray()))
                .ToList()),

            AppMenuNode.Menu("help", "Help",
                AppMenuNode.Item("help.project", "osXos on GitHub"),
                AppMenuNode.Item("help.about", "About osXos")),
        };
    }

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Linux)]
    public void The_tools_menu_reaches_every_tool_on_the_platform(OSKind os)
    {
        var registry = new ToolRegistry(os, os switch
        {
            OSKind.Windows => ToolCatalog.Windows(new FakeRunner(), new FakeExplorerSettings()),
            OSKind.MacOS => ToolCatalog.MacOS(new FakeRunner()),
            _ => ToolCatalog.Linux(new FakeRunner()),
        });

        var ids = Tree(os).SelectMany(n => n.Ids()).ToList();

        foreach (var tool in registry.Tools)
            Assert.Contains($"tool.{tool.Id}", ids);
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
