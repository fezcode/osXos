using OsXos.Tools;
using OsXos.Tools.Windows;
using OsXos.ViewModels;
using Xunit;
using MacDarkMode = OsXos.Tools.MacOS.DarkModeTool;
using WindowsDarkMode = OsXos.Tools.Windows.DarkModeTool;

namespace OsXos.Tests;

/// <summary>
/// The state pill: every toggle says what its setting is right now, in words about
/// the thing itself, and says plainly when that thing is not on this machine.
/// </summary>
public class StateTests
{
    static RegistryTweakTool Tweak(RegistryTweak tweak, FakeRegistry registry, FakeRunner? runner = null) =>
        new(tweak, registry, new FakeUserRegistry(), new FakeShellController(),
            runner ?? new FakeRunner(), new ElevationService(runner ?? new FakeRunner()));

    const string WindowsAI = @"Software\Policies\Microsoft\Windows\WindowsAI";

    static FakeRunner RecallRunner(string installState, int exit = 0) =>
        new FakeRunner().Returns(Availability.RecallQuery.Display, exit, installState);

    [Fact]
    public async Task Recall_says_it_is_not_on_this_pc_when_windows_has_no_recall_feature()
    {
        var state = await Tweak(OsXosTweaks.Recall, new FakeRegistry(), RecallRunner("")).ReadStateAsync(default);

        Assert.Equal("Not on this PC", state.Label);
        Assert.Equal(StateTone.Unavailable, state.Tone);
        Assert.Contains("Copilot+", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recall_reads_on_or_off_where_it_exists()
    {
        var registry = new FakeRegistry();
        var tool = Tweak(OsXosTweaks.Recall, registry, RecallRunner("2\n"));
        Assert.Equal("Recall on", (await tool.ReadStateAsync(default)).Label);

        registry.Set(RegHive.CurrentUser, WindowsAI, "DisableAIDataAnalysis", 1)
                .Set(RegHive.CurrentUser, WindowsAI, "DisableClickToDo", 1);
        Assert.Equal("Recall off", (await tool.ReadStateAsync(default)).Label);
    }

    [Fact]
    public async Task A_failed_recall_query_does_not_claim_recall_is_missing()
    {
        var state = await Tweak(OsXosTweaks.Recall, new FakeRegistry(), RecallRunner("", exit: 1)).ReadStateAsync(default);
        Assert.NotEqual(StateTone.Unavailable, state.Tone);
    }

    [Fact]
    public async Task The_review_leads_with_why_when_the_feature_is_missing_and_carries_the_state()
    {
        var preview = await Tweak(OsXosTweaks.Recall, new FakeRegistry(), RecallRunner("")).InspectAsync(default);

        Assert.StartsWith("Recall is not part of Windows on this PC", preview.Summary, StringComparison.Ordinal);
        Assert.Equal("Not on this PC", preview.State!.Label);
        Assert.True(preview.CanRun); // harmless to set, and ready if it arrives
    }

    [Fact]
    public async Task A_partly_applied_tweak_says_how_much()
    {
        var registry = new FakeRegistry().Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0);
        var state = await Tweak(OsXosTweaks.AdvertisingId, registry).ReadStateAsync(default);

        Assert.Equal("Partly applied", state.Label);
        Assert.Equal(StateTone.Partial, state.Tone);
        Assert.Contains("1 of 2", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Winutil_tweaks_speak_in_their_own_verb_and_preferences_say_on_or_off()
    {
        var registry = new FakeRegistry();
        var disable = WinUtilTweaks.All.First(t => t.Name == "Disable Background Apps");
        var pref = WinUtilTweaks.All.First(t => t.Name == "Game Mode");

        Assert.Equal("Enabled", (await Tweak(disable, registry).ReadStateAsync(default)).Label);
        Assert.Equal("Off", (await Tweak(pref, registry).ReadStateAsync(default)).Label);
    }

    [Fact]
    public async Task Every_windows_toggle_reports_a_state()
    {
        var toggles = TestCatalog.Windows().OfType<IHasState>().ToList();
        Assert.True(toggles.Count >= 40, $"only {toggles.Count} tools report state");

        foreach (var tool in toggles)
        {
            var state = await tool.ReadStateAsync(default);
            Assert.False(string.IsNullOrWhiteSpace(state.Label), $"{((ITool)tool).Id} has an empty state");
        }
    }

    [Fact]
    public async Task The_simple_toggles_name_their_state()
    {
        Assert.Equal("Light", (await new WindowsDarkMode(new FakeWindowsAppearance()).ReadStateAsync(default)).Label);
        Assert.Equal("Custom", (await new WindowsDarkMode(new FakeWindowsAppearance { AppsUseLightTheme = 0 }).ReadStateAsync(default)).Label);
        Assert.Equal("Hidden", (await new HiddenFilesTool(new FakeExplorerSettings()).ReadStateAsync(default)).Label);
        Assert.Equal("Windows 11", (await new ClassicContextMenuTool(new FakeUserRegistry(), new FakeShellController(), () => 26100).ReadStateAsync(default)).Label);
        Assert.Equal("Not on this PC", (await new ClassicContextMenuTool(new FakeUserRegistry(), new FakeShellController(), () => 19045).ReadStateAsync(default)).Label);
        Assert.Equal("Shown", (await new HideTaskbarTool(new FakeTaskbar(), new FakeUserRegistry(), new FakeKeeper(), () => "x", () => true).ReadStateAsync(default)).Label);

        var mac = new FakeRunner().Returns(MacDarkMode.ReadCommand.Display, 0, "Dark\n");
        Assert.Equal("Dark", (await new MacDarkMode(mac).ReadStateAsync(default)).Label);
    }

    [Fact]
    public async Task A_card_loads_its_state_and_a_cleanup_tool_shows_none()
    {
        var toggle = new ToolCardViewModel(new HiddenFilesTool(new FakeExplorerSettings { Hidden = 1, HideFileExt = 0 }), _ => Task.CompletedTask);
        var sweep = new ToolCardViewModel(new ScriptedTool("sweep"), _ => Task.CompletedTask);

        await toggle.State.LoadAsync(toggle.Tool);
        await sweep.State.LoadAsync(sweep.Tool);

        Assert.Equal("Shown", toggle.State.Label);
        Assert.True(toggle.State.IsOn);
        Assert.False(sweep.State.HasState);
    }

    [Fact]
    public async Task The_all_tools_page_shows_state_even_for_tools_whose_preview_lacks_it()
    {
        var hidden = new HiddenFilesTool(new FakeExplorerSettings());
        var page = new AllToolsViewModel(new ToolRegistry(OSKind.Windows, new ITool[] { hidden }), OSKind.Windows);

        await page.RefreshAsync();

        Assert.Equal("Hidden", page.Items.Single().State.Label);
    }
}
