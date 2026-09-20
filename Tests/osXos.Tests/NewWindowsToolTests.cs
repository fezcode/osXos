using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

public class RecentItemsToolTests
{
    [Fact]
    public async Task Inspect_covers_the_recent_folder_and_both_jump_list_folders()
    {
        using var dir = new TempDir();
        dir.File("report.docx.lnk", 500);
        dir.File(Path.Combine("AutomaticDestinations", "a1b2.automaticDestinations-ms"), 2000);
        dir.File(Path.Combine("CustomDestinations", "c3d4.customDestinations-ms"), 1000);

        var preview = await new RecentItemsTool(dir.Path).InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.Equal(3, preview.Items.Count);
        Assert.Equal(3500, preview.TotalBytes);
    }

    [Fact]
    public async Task The_jump_list_subfolders_are_not_listed_as_entries_themselves()
    {
        // Recent contains the other two as subdirectories; a recursive sweep would
        // have counted each jump list twice.
        using var dir = new TempDir();
        dir.File("only.lnk", 10);
        dir.Dir("AutomaticDestinations");
        dir.Dir("CustomDestinations");

        var preview = await new RecentItemsTool(dir.Path).InspectAsync(default);

        var item = Assert.Single(preview.Items);
        Assert.Equal("only.lnk", item.Label);
    }

    [Fact]
    public async Task Blocks_when_Windows_is_remembering_nothing()
    {
        using var dir = new TempDir();
        Assert.False((await new RecentItemsTool(dir.Path).InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Run_deletes_the_entries_it_listed()
    {
        using var dir = new TempDir();
        dir.File("a.lnk", 10);
        dir.File(Path.Combine("AutomaticDestinations", "b.automaticDestinations-ms"), 20);

        var tool = new RecentItemsTool(dir.Path);
        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Empty(FileSweep.Files(dir.Path, "*"));
        Assert.Empty(FileSweep.Files(Path.Combine(dir.Path, "AutomaticDestinations"), "*"));
    }
}

public class ExplorerHistoryToolTests
{
    const string TypedPaths = @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths";
    const string WordWheel = @"Software\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery";
    const string RunMru = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";

    static FakeRegistry Populated() => new FakeRegistry()
        .Set(RegHive.CurrentUser, TypedPaths, "url1", @"D:\Workhammer")
        .Set(RegHive.CurrentUser, TypedPaths, "url2", @"C:\Temp")
        .Set(RegHive.CurrentUser, WordWheel, "0", "invoice")
        .Set(RegHive.CurrentUser, RunMru, "a", "cmd\\1")
        .Set(RegHive.CurrentUser, RunMru, "MRUList", "a");

    [Fact]
    public async Task Inspect_lists_every_remembered_entry_across_the_three_keys()
    {
        var preview = await new ExplorerHistoryTool(Populated()).InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.Equal(5, preview.Items.Count);
        Assert.Contains(preview.Items, i => i.Detail == @"D:\Workhammer");
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Explorer search history", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_reads_without_deleting_anything()
    {
        var registry = Populated();
        await new ExplorerHistoryTool(registry).InspectAsync(default);
        Assert.Empty(registry.Deleted);
    }

    [Fact]
    public async Task The_RunMRU_ordering_value_is_shown_as_ordering_not_as_a_command()
    {
        var preview = await new ExplorerHistoryTool(Populated()).InspectAsync(default);

        var mruList = preview.Items.Single(i => i.Label.EndsWith("MRUList", StringComparison.Ordinal));
        Assert.Equal("(ordering)", mruList.Detail);
    }

    [Fact]
    public async Task Blocks_when_there_is_no_history()
    {
        Assert.False((await new ExplorerHistoryTool(new FakeRegistry()).InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Run_clears_all_three_lists()
    {
        var registry = Populated();
        var tool = new ExplorerHistoryTool(registry);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Empty(registry.ValueNames(RegHive.CurrentUser, TypedPaths));
        Assert.Empty(registry.ValueNames(RegHive.CurrentUser, WordWheel));
        Assert.Empty(registry.ValueNames(RegHive.CurrentUser, RunMru));
    }

    [Fact]
    public async Task Run_touches_nothing_outside_the_three_keys()
    {
        var registry = Populated()
            .Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1);

        var tool = new ExplorerHistoryTool(registry);
        await tool.RunAsync(await tool.InspectAsync(default), default);

        // Deleting the wrong Explorer key would take the user's shell settings with it.
        Assert.Equal(1, registry.GetValue(
            RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden"));
    }
}

public class DeveloperStatusToolTests
{
    [Fact]
    public async Task Reports_long_paths_and_developer_mode_when_both_are_on()
    {
        var registry = new FakeRegistry()
            .Set(RegHive.LocalMachine, DeveloperStatusTool.FileSystemKey, "LongPathsEnabled", 1)
            .Set(RegHive.LocalMachine, DeveloperStatusTool.AppModelUnlockKey,
                 "AllowDevelopmentWithoutDevLicense", 1);

        var preview = await new DeveloperStatusTool(registry).InspectAsync(default);

        Assert.Contains(preview.Items, i => i.Label.StartsWith("Long path", StringComparison.Ordinal)
                                         && i.Detail == "enabled");
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Developer Mode", StringComparison.Ordinal)
                                         && i.Detail == "enabled");
    }

    [Fact]
    public async Task An_unset_value_reads_as_not_set_rather_than_enabled()
    {
        // The dangerous failure would be reporting a missing key as "on".
        var preview = await new DeveloperStatusTool(new FakeRegistry()).InspectAsync(default);

        var longPaths = preview.Items.First(i => i.Label.StartsWith("Long path", StringComparison.Ordinal));
        Assert.StartsWith("not set", longPaths.Detail);
        Assert.Contains("260", longPaths.Detail);
    }

    [Fact]
    public async Task Running_the_report_changes_nothing()
    {
        var registry = new FakeRegistry()
            .Set(RegHive.LocalMachine, DeveloperStatusTool.FileSystemKey, "LongPathsEnabled", 0);
        var tool = new DeveloperStatusTool(registry);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Empty(registry.Deleted);
        Assert.Contains(result.Lines, l => l.Contains("For developers", StringComparison.Ordinal));
    }

    [Fact]
    public void The_report_is_never_destructive_and_never_elevated()
    {
        // RequiresElevation is a default interface member, so it is reached through
        // ITool rather than the concrete type - which is exactly how the app sees it.
        ITool tool = new DeveloperStatusTool(new FakeRegistry());
        Assert.False(tool.IsDestructive);
        Assert.False(tool.RequiresElevation);
    }
}

public class PathHealthToolTests
{
    static bool Exists(string p) => p.StartsWith("GOOD", StringComparison.Ordinal);

    [Fact]
    public void A_missing_folder_is_flagged()
    {
        var entries = PathHealthTool.Analyse("GOOD1;BAD1", Exists);

        Assert.False(entries[0].Missing);
        Assert.True(entries[1].Missing);
    }

    [Fact]
    public void A_repeated_entry_is_flagged_only_the_second_time()
    {
        var entries = PathHealthTool.Analyse("GOOD1;GOOD1", Exists);

        Assert.False(entries[0].Duplicate);
        Assert.True(entries[1].Duplicate);
    }

    [Fact]
    public void A_trailing_separator_does_not_make_a_new_entry()
    {
        // C:\bin and C:\bin\ are the same directory; treating them as different
        // would hide a real duplicate.
        var entries = PathHealthTool.Analyse(@"GOOD1;GOOD1\", Exists);
        Assert.True(entries[1].Duplicate);
    }

    [Fact]
    public void Quotes_and_whitespace_are_stripped_before_anything_is_judged()
    {
        var entries = PathHealthTool.Analyse("  \"GOOD1\"  ", Exists);

        Assert.Single(entries);
        Assert.Equal("GOOD1", entries[0].Value);
        Assert.True(entries[0].IsHealthy);
    }

    [Fact]
    public void An_empty_entry_is_called_out_as_the_current_directory()
    {
        var entries = PathHealthTool.Analyse("GOOD1;;GOOD2", Exists);

        Assert.True(entries[1].Empty);
        Assert.Contains("current directory", entries[1].Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_PATH_yields_nothing_rather_than_throwing()
    {
        Assert.Empty(PathHealthTool.Analyse(null, Exists));
        Assert.Empty(PathHealthTool.Analyse("", Exists));
    }

    [Fact]
    public async Task A_healthy_PATH_reports_as_healthy()
    {
        var tool = new PathHealthTool(() => Path.GetTempPath());
        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal("PATH is healthy", result.Headline);
    }

    [Fact]
    public async Task A_broken_PATH_names_the_entries_without_changing_them()
    {
        var path = Path.GetTempPath() + Path.PathSeparator + @"Z:\does\not\exist";
        var tool = new PathHealthTool(() => path);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Contains("attention", result.Headline, StringComparison.Ordinal);
        Assert.Contains(result.Lines, l => l.Contains(@"Z:\does\not\exist", StringComparison.Ordinal));
        Assert.Contains(result.Lines, l => l.Contains("does not edit PATH", StringComparison.Ordinal));
    }
}

public class RecycleBinToolTests
{
    [Fact]
    public async Task Inspect_reports_the_count_and_size_Windows_gives_it()
    {
        var bin = new FakeRecycleBin { State = new RecycleBinState(3_221_225_472, 1_204) };

        var preview = await new RecycleBinTool(bin).InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.Contains("1,204", preview.Summary);
        Assert.Contains("3 GB", preview.Summary);
    }

    [Fact]
    public async Task Blocks_on_an_already_empty_bin()
    {
        var preview = await new RecycleBinTool(new FakeRecycleBin()).InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    [Fact]
    public async Task Run_empties_it_once_and_reports_what_went()
    {
        var bin = new FakeRecycleBin { State = new RecycleBinState(1024 * 1024, 10) };
        var tool = new RecycleBinTool(bin);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal(1, bin.EmptyCount);
        Assert.Contains("1 MB", result.Headline);
    }

    [Fact]
    public async Task What_could_not_be_removed_is_reported_rather_than_assumed_gone()
    {
        // Re-querying afterwards is the point: reporting the requested figure would
        // overstate what actually happened when a file is held open.
        var bin = new FakeRecycleBin
        {
            State = new RecycleBinState(1000, 10),
            Remaining = new RecycleBinState(400, 4),
        };
        var tool = new RecycleBinTool(bin);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Contains(result.Lines, l => l.Contains("Removed 6 of 10", StringComparison.Ordinal));
        Assert.Contains(result.Lines, l => l.Contains("4 item(s) remain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refusal_from_the_shell_is_a_failure()
    {
        var bin = new FakeRecycleBin { State = new RecycleBinState(100, 1), EmptySucceeds = false };
        var tool = new RecycleBinTool(bin);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
    }
}

public class UpdateCacheToolTests
{
    [Fact]
    public void The_elevated_command_stops_the_service_deletes_and_starts_it_again()
    {
        var command = UpdateCacheTool.CommandFor(@"C:\Windows\SoftwareDistribution\Download");

        Assert.Equal("cmd.exe", command.File);
        Assert.Equal("/c", command.Args[0]);
        // One command, so the user sees one UAC prompt rather than three.
        Assert.Equal(
            @"net stop wuauserv && rd /s /q ""C:\Windows\SoftwareDistribution\Download"" && net start wuauserv",
            command.Args[1]);
    }

    [Fact]
    public async Task Inspect_blocks_when_the_cache_folder_is_absent()
    {
        using var dir = new TempDir();
        var tool = new UpdateCacheTool(dir.Sub("nope"), new ElevationService(new FakeRunner()));

        Assert.False((await tool.InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Inspect_blocks_when_the_cache_folder_is_empty()
    {
        using var dir = new TempDir();
        var tool = new UpdateCacheTool(dir.Path, new ElevationService(new FakeRunner()));

        Assert.False((await tool.InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Inspect_measures_the_cache_and_shows_the_command()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("abc", "update.cab"), 4096);

        var tool = new UpdateCacheTool(dir.Path, new ElevationService(new FakeRunner()));
        var preview = await tool.InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.True(preview.NeedsElevation);
        Assert.Contains(preview.Items, i => i.Bytes == 4096);
        Assert.Contains(preview.Items, i => i.Detail!.Contains("net stop wuauserv", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_system_that_cannot_elevate_is_given_the_command_to_run_itself()
    {
        using var dir = new TempDir();
        dir.File("update.cab", 100);

        var tool = new UpdateCacheTool(dir.Path, new CannotElevate());
        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains("net stop wuauserv", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_declined_UAC_prompt_is_reported_as_cancelled_not_as_a_failure()
    {
        using var dir = new TempDir();
        dir.File("update.cab", 100);

        // 1223 is ERROR_CANCELLED. Saying no is a choice, not a fault of the tool.
        var tool = new UpdateCacheTool(dir.Path, new ScriptedElevation(new ProcessOutcome(1223, "", "")));
        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Equal("Cancelled", result.Headline);
        Assert.Contains(result.Lines, l => l.Contains("nothing was changed", StringComparison.Ordinal));
    }

    sealed class CannotElevate : IElevationService
    {
        public bool IsElevated => false;
        public bool CanElevate => false;
        public string PromptDescription => "no mechanism";
        public Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct) =>
            throw new InvalidOperationException("must not be called when CanElevate is false");
    }

    sealed class ScriptedElevation : IElevationService
    {
        readonly ProcessOutcome _outcome;
        public ScriptedElevation(ProcessOutcome outcome) => _outcome = outcome;

        public bool IsElevated => false;
        public bool CanElevate => true;
        public string PromptDescription => "a prompt";
        public Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct) =>
            Task.FromResult(_outcome);
    }
}

public class ShellToolTests
{
    [Fact]
    public async Task Restarting_Explorer_deletes_nothing()
    {
        var shell = new FakeShellController();
        var tool = new RestartExplorerTool(shell);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.True(result.Ok);
        Assert.Equal(1, shell.StopCount);
        Assert.Equal(1, shell.StartCount);
        Assert.False(tool.IsDestructive);
        Assert.Contains(preview.Items, i => i.Detail == "Nothing.");
    }

    [Fact]
    public async Task A_shell_that_does_not_come_back_is_a_failure_with_a_way_out()
    {
        var tool = new RestartExplorerTool(new FakeShellController { StartSucceeds = false });
        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains("Task", StringComparison.OrdinalIgnoreCase)
                                        || l.Contains("explorer.exe", StringComparison.OrdinalIgnoreCase));
    }

    static FakeRegistry WithExtensions() => new FakeRegistry()
        .Set(RegHive.CurrentUser, $@"{OpenWithCacheTool.FileExtsKey}\.txt\UserChoice", "ProgId", "txtfile")
        .Set(RegHive.CurrentUser, $@"{OpenWithCacheTool.FileExtsKey}\.md\OpenWithList", "a", "Code.exe")
        .AddKey(RegHive.CurrentUser, $@"{OpenWithCacheTool.FileExtsKey}\.log");

    [Fact]
    public async Task Open_with_inspect_lists_each_extension_with_what_is_being_given_up()
    {
        var preview = await new OpenWithCacheTool(WithExtensions(), new FakeShellController())
            .InspectAsync(default);

        Assert.Equal(3, preview.Items.Count);
        Assert.Equal("default: txtfile", preview.Items.Single(i => i.Label == ".txt").Detail);
        Assert.Equal("1 remembered program(s)", preview.Items.Single(i => i.Label == ".md").Detail);
        Assert.Equal("no default set", preview.Items.Single(i => i.Label == ".log").Detail);
    }

    [Fact]
    public async Task Open_with_blocks_when_there_is_no_history()
    {
        var preview = await new OpenWithCacheTool(new FakeRegistry(), new FakeShellController())
            .InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    [Fact]
    public async Task Open_with_run_clears_the_extensions_and_restarts_the_shell()
    {
        var registry = WithExtensions();
        var shell = new FakeShellController();
        var tool = new OpenWithCacheTool(registry, shell);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Empty(registry.SubKeyNames(RegHive.CurrentUser, OpenWithCacheTool.FileExtsKey));
        Assert.Equal(1, shell.StartCount);
    }

    [Fact]
    public async Task Open_with_leaves_the_rest_of_the_Explorer_key_alone()
    {
        // Deleting the wrong subtree here would take the user's shell settings out
        // along with their file associations.
        var registry = WithExtensions()
            .Set(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1);

        var tool = new OpenWithCacheTool(registry, new FakeShellController());
        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Equal(1, registry.GetValue(
            RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden"));
    }
}
