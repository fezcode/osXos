using OsXos.Tools;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// Well-formedness of the tool catalogue, for all three platforms at once. These are
/// the tests that catch a macOS or Linux tool being wrong in a way that would only
/// otherwise surface on that machine.
/// </summary>
public class CatalogTests
{
    public static TheoryData<OSKind> AllPlatforms => new() { OSKind.Windows, OSKind.MacOS, OSKind.Linux };

    static IReadOnlyList<ITool> Build(OSKind os)
    {
        var runner = new FakeRunner();
        return os switch
        {
            OSKind.Windows => TestCatalog.Windows(runner),
            OSKind.MacOS => ToolCatalog.MacOS(runner),
            _ => ToolCatalog.Linux(runner),
        };
    }

    [Fact]
    public void Every_tool_id_is_unique_across_all_platforms()
    {
        var ids = AllPlatforms.SelectMany(row => Build((OSKind)row[0])).Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Every_tool_claims_the_platform_it_was_built_for(OSKind os)
    {
        Assert.All(Build(os), t => Assert.Equal(os, t.Platform));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Every_tool_sits_in_a_category_that_platform_defines(OSKind os)
    {
        // Linux has Packages and Services where Windows and macOS have Privacy and
        // Developer, so a copy-pasted tool landing in the wrong one is a real risk.
        Assert.All(Build(os), t => Assert.NotNull(CategoryCatalog.Find(os, t.Category)));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Every_tool_explains_itself(OSKind os)
    {
        Assert.All(Build(os), t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name), $"{t.Id} has no name");
            Assert.False(string.IsNullOrWhiteSpace(t.Summary), $"{t.Id} has no summary");
            Assert.False(string.IsNullOrWhiteSpace(t.IconKey), $"{t.Id} has no icon");

            // The whole premise of the app is that a tool says what it will do
            // before it does it. Two steps is the floor.
            Assert.True(t.Steps.Count >= 2, $"{t.Id} has {t.Steps.Count} step(s)");
            Assert.All(t.Steps, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Title), $"{t.Id} has an unnamed step");
                Assert.False(string.IsNullOrWhiteSpace(s.Detail), $"{t.Id}/{s.Title} has no detail");
            });
        });
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Destructive_tools_carry_a_warning(OSKind os)
    {
        Assert.All(Build(os).Where(t => t.IsDestructive), t =>
            Assert.False(string.IsNullOrWhiteSpace(t.Warning),
                $"{t.Id} is destructive but warns about nothing"));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Tool_ids_are_prefixed_with_their_platform(OSKind os)
    {
        var prefix = os switch
        {
            OSKind.Windows => "windows.",
            OSKind.MacOS => "macos.",
            _ => "linux.",
        };
        Assert.All(Build(os), t => Assert.StartsWith(prefix, t.Id, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Every_platform_defines_seven_categories(OSKind os)
    {
        Assert.Equal(7, CategoryCatalog.For(os).Count);
        Assert.All(CategoryCatalog.For(os), c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.IconKey));
        });
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void CountFor_matches_the_catalogue(OSKind os)
    {
        Assert.Equal(Build(os).Count, ToolCatalog.CountFor(os));
    }
}

public class RegistryTests
{
    static ToolRegistry Registry(OSKind os) => new(os, os switch
    {
        OSKind.Windows => TestCatalog.Windows(),
        OSKind.MacOS => ToolCatalog.MacOS(new FakeRunner()),
        _ => ToolCatalog.Linux(new FakeRunner()),
    });

    [Theory]
    [InlineData(OSKind.Windows)]
    [InlineData(OSKind.MacOS)]
    [InlineData(OSKind.Linux)]
    public void Only_categories_with_a_tool_are_listed(OSKind os)
    {
        var registry = Registry(os);

        Assert.NotEmpty(registry.Categories);
        Assert.All(registry.Categories, c => Assert.NotEmpty(registry.InCategory(c.Category)));

        // The rule is that a category appears exactly when it has a tool - not that
        // some are always missing. Windows now fills all six; macOS and Linux do not.
        var populated = CategoryCatalog.For(os)
            .Where(c => registry.Tools.Any(t => t.Category == c.Category))
            .Select(c => c.Category);
        Assert.Equal(populated, registry.Categories.Select(c => c.Category));
    }

    [Fact]
    public void Categories_keep_their_taxonomy_order()
    {
        var registry = Registry(OSKind.Windows);
        var taxonomy = CategoryCatalog.For(OSKind.Windows).Select(c => c.Category).ToList();
        var listed = registry.Categories.Select(c => c.Category).ToList();

        Assert.Equal(listed.OrderBy(c => taxonomy.IndexOf(c)).ToList(), listed);
    }

    [Fact]
    public void Every_tool_reaches_exactly_one_category()
    {
        var registry = Registry(OSKind.Windows);
        var reachable = registry.Categories.SelectMany(c => registry.InCategory(c.Category)).ToList();

        Assert.Equal(registry.Tools.Count, reachable.Count);
        Assert.Equal(registry.Tools.OrderBy(t => t.Id).Select(t => t.Id),
                     reachable.OrderBy(t => t.Id).Select(t => t.Id));
    }

    [Fact]
    public void Search_matches_name_summary_and_category()
    {
        var registry = Registry(OSKind.Windows);

        Assert.Contains(registry.Search("icon cache"), t => t.Id == "windows.icon-cache");
        Assert.Contains(registry.Search("DNS"), t => t.Id == "windows.flush-dns");
        // "Explorer & Shell" is the category name, not a word in the tool's own text.
        Assert.Contains(registry.Search("explorer"), t => t.Id == "windows.hidden-files");
        Assert.Empty(registry.Search("defragment the tape drive"));
    }

    [Fact]
    public void Empty_search_returns_everything()
    {
        var registry = Registry(OSKind.Windows);
        Assert.Equal(registry.Tools.Count, registry.Search("").Count);
        Assert.Equal(registry.Tools.Count, registry.Search("   ").Count);
        Assert.Equal(registry.Tools.Count, registry.Search(null).Count);
    }

    [Fact]
    public void Search_ignores_case_and_surrounding_space()
    {
        var registry = Registry(OSKind.Windows);
        Assert.Equal(
            registry.Search("icon cache").Select(t => t.Id),
            registry.Search("  ICON CACHE  ").Select(t => t.Id));
    }
}
