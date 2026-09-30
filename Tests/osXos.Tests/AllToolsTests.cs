using OsXos.Tools;
using OsXos.Tools.Ai;
using OsXos.ViewModels;
using Xunit;

namespace OsXos.Tests;

/// <summary>A tool whose every answer is set by the test, and which records its runs.</summary>
sealed class ScriptedTool : ITool
{
    public ScriptedTool(string id, ToolCategory category = ToolCategory.Maintenance) { Id = "windows." + id; Category = category; }

    public string Id { get; }
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category { get; }
    public string Name => Id;
    public string Summary => "scripted";
    public string IconKey => "IconTrash";
    public string? Warning => null;
    public bool IsDestructive { get; init; }
    public bool RequiresElevation { get; init; }
    public bool IsReadOnly { get; init; }
    public IReadOnlyCollection<string> Covers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ToolStep> Steps { get; } = new[] { new ToolStep("a", "b"), new ToolStep("c", "d") };

    public ToolPreview Preview { get; set; } = new(new[] { new PreviewItem("x", "y", 1024) }, "Will do the thing.");
    public ToolResult Result { get; set; } = ToolResult.Success("Did the thing");
    public Exception? Throws { get; set; }
    public List<ToolPreview> RanWith { get; } = new();
    public int Inspections { get; private set; }
    public List<string>? Log { get; init; }

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        Inspections++;
        return Task.FromResult(Preview);
    }

    public Task<ToolResult> RunAsync(ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        Log?.Add(Id);
        RanWith.Add(preview);
        if (Throws is not null) throw Throws;
        return Task.FromResult(Result);
    }
}

public class BatchRunnerTests
{
    [Fact]
    public async Task Tools_run_in_order_each_on_its_own_shown_preview()
    {
        var log = new List<string>();
        var a = new ScriptedTool("a") { Log = log };
        var b = new ScriptedTool("b") { Log = log };
        var shownA = new ToolPreview(new[] { new PreviewItem("only this") }, "shown");

        var outcomes = await BatchRunner.RunAsync(
            new[] { new BatchStep(a, shownA), new BatchStep(b, b.Preview) }, (_, _, _) => { });

        Assert.Equal(new[] { "windows.a", "windows.b" }, log);
        Assert.Same(shownA, Assert.Single(a.RanWith));
        Assert.All(outcomes, o => Assert.Equal(BatchStatus.Done, o.Status));
        Assert.Equal(0, a.Inspections); // no fresh scan behind the user's back
    }

    [Fact]
    public async Task A_failure_or_exception_does_not_stop_the_rest()
    {
        var a = new ScriptedTool("a") { Result = ToolResult.Failure("nope") };
        var b = new ScriptedTool("b") { Throws = new InvalidOperationException("boom") };
        var c = new ScriptedTool("c");

        var outcomes = await BatchRunner.RunAsync(
            new[] { a, b, c }.Select(t => new BatchStep(t, t.Preview)).ToList(), (_, _, _) => { });

        Assert.Equal(new[] { BatchStatus.Failed, BatchStatus.Failed, BatchStatus.Done }, outcomes.Select(o => o.Status));
        Assert.Contains("boom", outcomes[1].Result!.Lines);
    }

    [Fact]
    public async Task Stop_skips_everything_after_the_current_tool()
    {
        var stop = false;
        var a = new ScriptedTool("a");
        var b = new ScriptedTool("b");

        var outcomes = await BatchRunner.RunAsync(
            new[] { a, b }.Select(t => new BatchStep(t, t.Preview)).ToList(),
            (tool, status, _) => { if (tool == a && status == BatchStatus.Done) stop = true; },
            () => stop);

        Assert.Equal(new[] { BatchStatus.Done, BatchStatus.Skipped }, outcomes.Select(o => o.Status));
        Assert.Empty(b.RanWith);
    }

    [Fact]
    public async Task Reports_and_blocked_previews_are_never_run()
    {
        var report = new ScriptedTool("report") { IsReadOnly = true };
        var blocked = new ScriptedTool("blocked") { Preview = ToolPreview.Blocked("nothing here") };

        var outcomes = await BatchRunner.RunAsync(
            new[] { report, blocked }.Select(t => new BatchStep(t, t.Preview)).ToList(), (_, _, _) => { });

        Assert.All(outcomes, o => Assert.Equal(BatchStatus.Skipped, o.Status));
        Assert.Empty(report.RanWith);
        Assert.Empty(blocked.RanWith);
    }

    [Fact]
    public async Task Every_tool_is_reported_queued_first_then_as_it_goes()
    {
        var a = new ScriptedTool("a");
        var b = new ScriptedTool("b");
        var seen = new List<(string, BatchStatus)>();

        await BatchRunner.RunAsync(new[] { a, b }.Select(t => new BatchStep(t, t.Preview)).ToList(),
            (tool, status, _) => seen.Add((tool.Id, status)));

        Assert.Equal(new[]
        {
            ("windows.a", BatchStatus.Queued), ("windows.b", BatchStatus.Queued),
            ("windows.a", BatchStatus.Running), ("windows.a", BatchStatus.Done),
            ("windows.b", BatchStatus.Running), ("windows.b", BatchStatus.Done),
        }, seen);
    }

    [Fact]
    public void The_ai_tools_declare_which_narrower_ones_they_include()
    {
        var roots = new ProfileRoots("h", "s", "c", "t");
        var temp = new AiCleanupTool(OSKind.Windows, AiJob.Temp, roots);
        var caches = new AiCleanupTool(OSKind.Windows, AiJob.Caches, roots);
        var history = new AiCleanupTool(OSKind.Windows, AiJob.History, roots);
        var everything = new AiCleanupTool(OSKind.Windows, AiJob.Everything, roots);

        Assert.Equal(new[] { temp.Id }, caches.Covers);
        Assert.Equal(new[] { temp.Id, caches.Id, history.Id }.OrderBy(x => x), everything.Covers.OrderBy(x => x));
        Assert.Empty(((ITool)temp).Covers);
        Assert.Empty(((ITool)history).Covers);
    }
}

public class AllToolsViewModelTests
{
    static AllToolsViewModel Page(params ITool[] tools) => new(new ToolRegistry(OSKind.Windows, tools), OSKind.Windows);

    static async Task<AllToolsViewModel> Scanned(params ITool[] tools)
    {
        var page = Page(tools);
        await page.RefreshAsync();
        return page;
    }

    static BatchItemViewModel Row(AllToolsViewModel page, ITool tool) => page.Items.Single(i => i.Tool == tool);

    [Fact]
    public async Task Nothing_is_scanned_until_the_page_is_opened()
    {
        var tool = new ScriptedTool("a");
        var page = Page(tool);
        Assert.Equal(0, tool.Inspections);

        await page.RefreshAsync();

        Assert.Equal(1, tool.Inspections);
        Assert.Equal("Will do the thing.", Row(page, tool).StateText);
        Assert.Equal("1 KB", Row(page, tool).SizeText);
    }

    [Fact]
    public async Task Reports_and_tools_with_nothing_to_do_cannot_be_ticked()
    {
        var report = new ScriptedTool("report") { IsReadOnly = true };
        var blocked = new ScriptedTool("blocked") { Preview = ToolPreview.Blocked("Already clean.") };
        var page = await Scanned(report, blocked);

        Row(page, report).IsChecked = true;
        Row(page, blocked).IsChecked = true;

        Assert.False(Row(page, report).IsChecked);
        Assert.False(Row(page, blocked).IsChecked);
        Assert.Equal("Already clean.", Row(page, blocked).StateText);
        Assert.True(Row(page, blocked).HasNothingToDo);
        Assert.False(page.CanRunSelected);
    }

    [Fact]
    public async Task Ticking_a_covering_tool_unticks_and_disables_what_it_includes()
    {
        var narrow = new ScriptedTool("narrow");
        var wide = new ScriptedTool("wide") { Covers = new[] { "windows.narrow" } };
        var page = await Scanned(narrow, wide);

        Row(page, narrow).IsChecked = true;
        Row(page, wide).IsChecked = true;

        Assert.False(Row(page, narrow).IsChecked);
        Assert.False(Row(page, narrow).IsSelectable);
        Assert.Equal("Included in windows.wide", Row(page, narrow).CoveredText);

        Row(page, wide).IsChecked = false;
        Assert.True(Row(page, narrow).IsSelectable);
        Assert.False(Row(page, narrow).IsCovered);
    }

    [Fact]
    public async Task Select_all_ticks_everything_runnable_and_respects_covering()
    {
        var narrow = new ScriptedTool("narrow");
        var wide = new ScriptedTool("wide") { Covers = new[] { "windows.narrow" } };
        var report = new ScriptedTool("report") { IsReadOnly = true };
        var page = await Scanned(narrow, wide, report);

        page.SelectAllCommand.Execute().Subscribe();

        Assert.Equal(new[] { wide }, page.Selected.Select(i => i.Tool));
        Assert.Equal("1 tool selected · 1 KB to reclaim", page.SelectionText);
    }

    [Fact]
    public async Task The_confirmation_names_the_tools_and_warns_only_about_what_applies()
    {
        var sweep = new ScriptedTool("sweep") { IsDestructive = true };
        var admin = new ScriptedTool("admin") { RequiresElevation = true };
        var toggle = new ScriptedTool("toggle");
        var page = await Scanned(sweep, admin, toggle);
        foreach (var item in page.Items) item.IsChecked = true;

        page.ReviewCommand.Execute().Subscribe();

        Assert.True(page.ShowConfirm);
        Assert.Equal("Run 3 tools?", page.ConfirmTitle);
        Assert.Equal(new[] { "windows.sweep", "windows.admin", "windows.toggle" }, page.ConfirmNames);
        Assert.Contains(page.ConfirmNotes, n => n.StartsWith("1 of them deletes files permanently", StringComparison.Ordinal));
        Assert.Contains(page.ConfirmNotes, n => n.Contains("windows.admin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Running_marks_each_row_then_unticks_and_rescans_what_ran()
    {
        var ok = new ScriptedTool("ok");
        var bad = new ScriptedTool("bad") { Result = ToolResult.Failure("Could not do it") };
        var untouched = new ScriptedTool("untouched");
        var page = await Scanned(ok, bad, untouched);
        Row(page, ok).IsChecked = true;
        Row(page, bad).IsChecked = true;

        await page.RunSelectedAsync();

        Assert.Equal(BatchStatus.Done, Row(page, ok).Status);
        Assert.Equal("Did the thing", Row(page, ok).ResultText);
        Assert.Equal(BatchStatus.Failed, Row(page, bad).Status);
        Assert.Equal("Could not do it", Row(page, bad).ResultText);
        Assert.Null(Row(page, untouched).Status);
        Assert.Empty(untouched.RanWith);

        Assert.Equal(0, page.SelectedCount);
        Assert.Equal("1 done · 1 failed", page.RunSummary);
        Assert.Equal(2, ok.Inspections);        // scanned on open, and again after it ran
        Assert.Equal(1, untouched.Inspections); // never ran, never rescanned
    }

    [Fact]
    public async Task A_finished_batch_opens_a_report_of_what_ran_and_what_each_said()
    {
        var ok = new ScriptedTool("ok") { Result = ToolResult.Success("Freed 1 KB", "Removed 3 items.", "Nothing was held open.") };
        var bad = new ScriptedTool("bad") { Result = ToolResult.Failure("Could not do it", "Access denied.") };
        var untouched = new ScriptedTool("untouched");
        var page = await Scanned(ok, bad, untouched);
        Row(page, ok).IsChecked = true;
        Row(page, bad).IsChecked = true;

        await page.RunSelectedAsync();

        Assert.True(page.ShowReport);
        Assert.Equal("Ran 2 tools — 1 done · 1 failed", page.ReportTitle);
        Assert.Equal(new[] { "windows.ok", "windows.bad" }, page.Report.Select(r => r.Name));

        var first = page.Report[0];
        Assert.True(first.Succeeded);
        Assert.Equal("Freed 1 KB", first.Headline);
        Assert.Equal(new[] { "Removed 3 items.", "Nothing was held open." }, first.Lines);

        var second = page.Report[1];
        Assert.True(second.Failed);
        Assert.Equal("Failed", second.StatusText);
        Assert.Equal(new[] { "Access denied." }, second.Lines);
    }

    [Fact]
    public async Task The_report_closes_and_can_be_opened_again_until_the_next_batch()
    {
        var tool = new ScriptedTool("a");
        var page = await Scanned(tool);
        Row(page, tool).IsChecked = true;
        await page.RunSelectedAsync();

        page.CloseReportCommand.Execute().Subscribe();
        Assert.False(page.ShowReport);

        page.OpenReportCommand.Execute().Subscribe();
        Assert.True(page.ShowReport);
        Assert.Single(page.Report);
    }

    [Fact]
    public async Task Skipped_tools_appear_in_the_report_too()
    {
        var a = new ScriptedTool("a");
        var b = new ScriptedTool("b");
        var page = await Scanned(a, b);
        Row(page, a).IsChecked = true;
        Row(page, b).IsChecked = true;
        a.Log?.Clear();

        // Stop as soon as the first tool starts, so the second is skipped.
        Row(page, a).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchItemViewModel.Status) && Row(page, a).Status == BatchStatus.Running)
                page.StopCommand.Execute().Subscribe();
        };
        await page.RunSelectedAsync();

        Assert.Equal(new[] { BatchStatus.Done, BatchStatus.Skipped }, page.Report.Select(r => r.Status));
        Assert.Equal("Skipped", page.Report[1].StatusText);
        Assert.Contains("1 skipped", page.ReportTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_acts_on_the_state_the_page_showed()
    {
        var tool = new ScriptedTool("a");
        var page = await Scanned(tool);
        var shown = Row(page, tool).Preview;
        Row(page, tool).IsChecked = true;

        tool.Preview = new ToolPreview(new[] { new PreviewItem("appeared later") }, "changed");
        await page.RunSelectedAsync();

        Assert.Same(shown, Assert.Single(tool.RanWith));
    }

    [Fact]
    public async Task A_scan_that_throws_says_so_on_its_row()
    {
        var broken = new ThrowingInspect();
        var page = await Scanned(broken);

        Assert.StartsWith("Could not check: disk on fire", Row(page, broken).StateText, StringComparison.Ordinal);
        Assert.False(Row(page, broken).IsSelectable);
    }

    sealed class ThrowingInspect : ITool
    {
        public string Id => "windows.broken";
        public OSKind Platform => OSKind.Windows;
        public ToolCategory Category => ToolCategory.Maintenance;
        public string Name => "Broken";
        public string Summary => "";
        public string IconKey => "IconTrash";
        public string? Warning => null;
        public bool IsDestructive => false;
        public IReadOnlyList<ToolStep> Steps => Array.Empty<ToolStep>();
        public Task<ToolPreview> InspectAsync(CancellationToken ct) => throw new IOException("disk on fire");
        public Task<ToolResult> RunAsync(ToolPreview p, CancellationToken ct, IProgress<ToolProgress>? pr = null) =>
            throw new InvalidOperationException();
    }

    [Fact]
    public void Rows_are_grouped_by_category_in_taxonomy_order()
    {
        var page = Page(
            new ScriptedTool("dev", ToolCategory.Developer),
            new ScriptedTool("clean", ToolCategory.Maintenance),
            new ScriptedTool("ai", ToolCategory.AI));

        Assert.Equal(new[] { "Maintenance", "Developer", "AI Assistants" }, page.Groups.Select(g => g.Name));
    }
}

public class ReadOnlyToolTests
{
    /// <summary>
    /// Exactly which tools are reports. A list, like the elevation allow-list, so a
    /// tool that starts changing things cannot keep wearing the pill by accident.
    /// </summary>
    static readonly string[] Reports =
    {
        "windows.developer-status", "windows.path-health", "windows.startup-apps", "linux.failed-services",
    };

    [Fact]
    public void Only_the_reports_are_read_only()
    {
        var runner = new FakeRunner();
        var all = TestCatalog.Windows().Concat(ToolCatalog.MacOS(runner)).Concat(ToolCatalog.Linux(runner));

        Assert.Equal(Reports.OrderBy(x => x), all.Where(t => t.IsReadOnly).Select(t => t.Id).OrderBy(x => x));
    }

    [Fact]
    public void A_report_is_never_destructive_and_never_needs_admin()
    {
        var runner = new FakeRunner();
        var reports = TestCatalog.Windows().Concat(ToolCatalog.Linux(runner)).Where(t => t.IsReadOnly);

        Assert.All(reports, t =>
        {
            Assert.False(t.IsDestructive);
            Assert.False(t.RequiresElevation);
        });
    }

    [Fact]
    public void The_card_carries_the_flag_for_its_pill()
    {
        var report = new ScriptedTool("r") { IsReadOnly = true };
        Assert.True(new ToolCardViewModel(report, _ => Task.CompletedTask).IsReadOnly);
        Assert.False(new ToolCardViewModel(new ScriptedTool("t"), _ => Task.CompletedTask).IsReadOnly);
    }
}
