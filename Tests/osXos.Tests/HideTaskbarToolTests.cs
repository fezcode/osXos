using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// Hide the Taskbar, against a fake taskbar, a fake keeper and an in-memory
/// registry — the real taskbar on the machine running the tests never moves.
/// </summary>
public class HideTaskbarToolTests
{
    const string Exe = @"C:\Program Files\osXos\osXos.exe";

    static HideTaskbarTool Tool(FakeTaskbar taskbar, FakeUserRegistry registry, FakeKeeper? keeper = null,
        bool hisashi = true, string? exe = Exe) =>
        new(taskbar, registry, keeper ?? new FakeKeeper(), () => exe, () => hisashi);

    static async Task<ToolResult> InspectAndRun(ITool tool) =>
        await tool.RunAsync(await tool.InspectAsync(default), default);

    [Fact]
    public async Task Hiding_turns_on_auto_hide_hides_every_window_and_starts_the_keeper()
    {
        var taskbar = new FakeTaskbar();
        var registry = new FakeUserRegistry();
        var keeper = new FakeKeeper();

        var result = await InspectAndRun(Tool(taskbar, registry, keeper));

        Assert.True(result.Ok);
        Assert.True(taskbar.IsHidden);
        Assert.True(taskbar.AutoHide);
        Assert.Equal($"\"{Exe}\" --hide-taskbar", registry.GetString(HideTaskbarTool.RunKey, HideTaskbarTool.RunValue));
        Assert.Equal(new[] { Exe }, keeper.Started);
        Assert.Contains(result.Lines, l => l.Contains("2 taskbar windows", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Running_twice_stops_the_keeper_and_puts_everything_back()
    {
        var taskbar = new FakeTaskbar { AutoHide = false };
        var registry = new FakeUserRegistry();
        var keeper = new FakeKeeper();
        var tool = Tool(taskbar, registry, keeper);

        await InspectAndRun(tool);
        var back = await InspectAndRun(tool);

        Assert.True(back.Ok);
        Assert.Equal(1, keeper.Stops);
        Assert.False(keeper.IsRunning);
        Assert.False(taskbar.IsHidden);
        Assert.False(taskbar.AutoHide);
        Assert.Null(registry.GetString(HideTaskbarTool.RunKey, HideTaskbarTool.RunValue));
        Assert.Null(registry.GetDword(HideTaskbarTool.StateKey, HideTaskbarTool.AutoHideBeforeValue));
    }

    [Fact]
    public async Task Someone_who_already_used_auto_hide_keeps_it_after_undoing()
    {
        var taskbar = new FakeTaskbar { AutoHide = true };
        var registry = new FakeUserRegistry();
        var tool = Tool(taskbar, registry);

        await InspectAndRun(tool);
        await InspectAndRun(tool);

        Assert.False(taskbar.IsHidden);
        Assert.True(taskbar.AutoHide);
    }

    [Fact]
    public async Task A_showing_taskbar_while_set_to_hide_is_hidden_again_without_forgetting_auto_hide()
    {
        // The keeper not running (killed, say) lets a restarted Explorer's taskbar
        // show. Running the tool then re-hides it, and must not record osXos's own
        // "on" as though the user had chosen auto-hide.
        var taskbar = new FakeTaskbar { AutoHide = false };
        var registry = new FakeUserRegistry();
        var tool = Tool(taskbar, registry);

        await InspectAndRun(tool);
        taskbar.IsHidden = false;

        var preview = await tool.InspectAsync(default);
        Assert.Contains("Will hide it again", preview.Summary, StringComparison.Ordinal);
        await tool.RunAsync(preview, default);
        Assert.True(taskbar.IsHidden);

        await InspectAndRun(tool);
        Assert.False(taskbar.AutoHide);
    }

    [Fact]
    public async Task Inspect_changes_nothing()
    {
        var taskbar = new FakeTaskbar();
        var registry = new FakeUserRegistry();
        var keeper = new FakeKeeper();

        await Tool(taskbar, registry, keeper).InspectAsync(default);

        Assert.False(taskbar.IsHidden);
        Assert.False(taskbar.AutoHide);
        Assert.Empty(registry.Strings);
        Assert.Empty(registry.Dwords);
        Assert.Empty(keeper.Started);
    }

    [Fact]
    public async Task It_says_when_hisashi_is_not_there_to_take_over()
    {
        var preview = await Tool(new FakeTaskbar(), new FakeUserRegistry(), hisashi: false).InspectAsync(default);

        var row = Assert.Single(preview.Items, i => i.Label == "Hisashi");
        Assert.StartsWith("not running", row.Detail, StringComparison.Ordinal);
        Assert.True(preview.CanRun);
    }

    [Fact]
    public async Task It_blocks_when_explorer_has_no_taskbar_or_osxos_cannot_find_itself()
    {
        Assert.False((await Tool(new FakeTaskbar { Exists = false }, new FakeUserRegistry()).InspectAsync(default)).CanRun);
        Assert.False((await Tool(new FakeTaskbar(), new FakeUserRegistry(), exe: null).InspectAsync(default)).CanRun);
    }

    [Fact]
    public void It_needs_no_elevation_and_is_not_destructive()
    {
        ITool tool = Tool(new FakeTaskbar(), new FakeUserRegistry());
        Assert.False(tool.RequiresElevation);
        Assert.False(tool.IsDestructive);
        Assert.Equal(ToolCategory.Shell, tool.Category);
        Assert.Contains(TestCatalog.Windows(), t => t.Id == "windows.hide-taskbar");
    }

    // ------------------------------------------------------------- keeper --

    /// <summary>Drives the keeper one tick at a time, running a scripted change before each.</summary>
    static void Keep(FakeTaskbar taskbar, params Action[] ticks)
    {
        var tick = 0;
        TaskbarKeeper.Run(taskbar, _ =>
        {
            if (tick >= ticks.Length) return true;
            ticks[tick++]();
            return false;
        });
    }

    [Fact]
    public void The_keeper_hides_the_taskbar_once_it_appears_at_sign_in()
    {
        var taskbar = new FakeTaskbar { Exists = false };

        Keep(taskbar,
            () => { },
            () => taskbar.Exists = true,   // Explorer draws it a couple of seconds in
            () => { });

        Assert.True(taskbar.IsHidden);
        Assert.True(taskbar.AutoHide);
    }

    [Fact]
    public void The_keeper_hides_a_new_taskbar_after_explorer_restarts()
    {
        var taskbar = new FakeTaskbar();

        Keep(taskbar,
            () => { },
            () => { taskbar.Handles.Clear(); taskbar.Handles.Add(300); taskbar.IsHidden = false; }, // Explorer restarted
            () => { });

        Assert.True(taskbar.IsHidden);
        Assert.Equal(2, taskbar.HideCalls);
    }

    [Fact]
    public void The_keeper_hides_the_taskbar_on_a_monitor_plugged_in_later()
    {
        var taskbar = new FakeTaskbar();

        Keep(taskbar,
            () => taskbar.Handles.Add(400),
            () => { });

        Assert.Equal(2, taskbar.HideCalls);
    }

    [Fact]
    public void The_keeper_never_re_hides_a_window_it_already_hid()
    {
        // If the user or Windows shows the same taskbar window again, the keeper
        // leaves it: only a new window means Explorer made a new taskbar.
        var taskbar = new FakeTaskbar();

        Keep(taskbar,
            () => taskbar.IsHidden = false,
            () => { },
            () => { });

        Assert.Equal(1, taskbar.HideCalls);
        Assert.False(taskbar.IsHidden);
    }

    [Fact]
    public void The_keeper_stops_when_told_to()
    {
        var taskbar = new FakeTaskbar();
        var waits = 0;

        TaskbarKeeper.Run(taskbar, _ => ++waits == 3);

        Assert.Equal(3, waits);
    }
}
