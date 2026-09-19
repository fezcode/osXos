using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

public class IconCacheToolTests
{
    static IconCacheTool Tool(TempDir dir, IShellController shell, string? legacy = null) =>
        new(dir.Path, legacy ?? dir.Sub("IconCache.db"), shell);

    [Fact]
    public async Task Inspect_finds_both_cache_families_and_the_legacy_database()
    {
        using var dir = new TempDir();
        dir.File("iconcache_16.db", 1000);
        dir.File("iconcache_256.db", 2000);
        dir.File("thumbcache_32.db", 4000);
        var legacy = dir.File("IconCache.db", 8000);

        var preview = await Tool(dir, new FakeShellController(), legacy).InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.Equal(4, preview.Items.Count);
        Assert.Equal(15000, preview.TotalBytes);
        Assert.Contains("4 files", preview.Summary);
    }

    [Fact]
    public async Task Inspect_leaves_unrelated_files_in_the_explorer_folder_alone()
    {
        using var dir = new TempDir();
        dir.File("iconcache_16.db", 100);
        dir.File("ExplorerStartupLog.etl", 9999);
        dir.File("thumbcache.db", 9999);   // no size suffix: not one of ours

        var preview = await Tool(dir, new FakeShellController()).InspectAsync(default);

        var item = Assert.Single(preview.Items);
        Assert.Equal("iconcache_16.db", item.Label);
    }

    [Fact]
    public async Task Inspect_blocks_when_there_is_no_cache_to_clear()
    {
        using var dir = new TempDir();

        var preview = await Tool(dir, new FakeShellController()).InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.NotNull(preview.Blocker);
        Assert.Empty(preview.Items);
    }

    [Fact]
    public async Task Run_stops_explorer_deletes_then_starts_it_again()
    {
        using var dir = new TempDir();
        dir.File("iconcache_16.db", 1024);
        dir.File("thumbcache_32.db", 1024);
        var shell = new FakeShellController();
        var tool = Tool(dir, shell);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.True(result.Ok);
        Assert.Equal(1, shell.StopCount);
        Assert.Equal(1, shell.StartCount);
        Assert.Empty(FileSweep.Files(dir.Path, "iconcache_*.db", "thumbcache_*.db"));
        Assert.Contains("2 KB", result.Headline);
    }

    [Fact]
    public async Task Run_fails_loudly_when_explorer_does_not_come_back()
    {
        using var dir = new TempDir();
        dir.File("iconcache_16.db", 512);
        var shell = new FakeShellController { StartSucceeds = false };
        var tool = Tool(dir, shell);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        // The cache did get cleared, but a user left without a desktop has not had a
        // successful run, whatever the deletion count says.
        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains("explorer.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Run_reports_failure_when_nothing_could_be_deleted()
    {
        using var dir = new TempDir();
        var path = dir.File("iconcache_16.db", 512);
        var tool = Tool(dir, new FakeShellController());
        var preview = await tool.InspectAsync(default);

        using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = await tool.RunAsync(preview, default);

        Assert.False(result.Ok);
        Assert.Equal("Nothing could be deleted", result.Headline);
    }
}

public class TempFolderToolTests
{
    [Fact]
    public async Task Inspect_lists_top_level_entries_with_sizes()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("installer-scratch", "payload.bin"), 2048);
        dir.File("stray.tmp", 1024);

        var preview = await new TempFolderTool(dir.Path).InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.Equal(2, preview.Items.Count);
        Assert.Equal(3072, preview.TotalBytes);
        Assert.Contains(dir.Path, preview.Summary);
    }

    [Fact]
    public async Task Inspect_blocks_on_an_already_empty_folder()
    {
        using var dir = new TempDir();
        var preview = await new TempFolderTool(dir.Path).InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    [Fact]
    public async Task Run_deletes_what_it_can_and_keeps_what_is_in_use()
    {
        using var dir = new TempDir();
        var held = dir.File("held.tmp", 100);
        dir.File("free.tmp", 100);
        var tool = new TempFolderTool(dir.Path);
        var preview = await tool.InspectAsync(default);

        using (var _ = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await tool.RunAsync(preview, default);
            Assert.True(result.Ok);
            Assert.Contains(result.Lines, l => l.Contains("1 left in place", StringComparison.Ordinal));
        }

        Assert.True(File.Exists(held));
    }
}

public class HiddenFilesToolTests
{
    [Fact]
    public async Task Inspect_reads_the_current_state_without_changing_it()
    {
        var settings = new FakeExplorerSettings { Hidden = 2, HideFileExt = 1 };

        var preview = await new HiddenFilesTool(settings).InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.Equal(2, settings.Hidden);
        Assert.Equal(1, settings.HideFileExt);
        Assert.Equal(0, settings.NotifyCount);
        Assert.Contains("show", preview.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_turns_both_on_when_either_is_hiding()
    {
        // The half-on case: extensions already shown, hidden files still hidden.
        var settings = new FakeExplorerSettings { Hidden = 2, HideFileExt = 0 };
        var tool = new HiddenFilesTool(settings);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal(1, settings.Hidden);
        Assert.Equal(0, settings.HideFileExt);
        Assert.Equal(1, settings.NotifyCount);
    }

    [Fact]
    public async Task Run_turns_both_off_only_when_both_are_already_on()
    {
        var settings = new FakeExplorerSettings { Hidden = 1, HideFileExt = 0 };
        var tool = new HiddenFilesTool(settings);

        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Equal(2, settings.Hidden);
        Assert.Equal(1, settings.HideFileExt);
    }

    [Fact]
    public async Task Running_twice_returns_to_where_it_started()
    {
        var settings = new FakeExplorerSettings { Hidden = 2, HideFileExt = 1 };
        var tool = new HiddenFilesTool(settings);

        await tool.RunAsync(await tool.InspectAsync(default), default);
        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Equal(2, settings.Hidden);
        Assert.Equal(1, settings.HideFileExt);
    }

    [Fact]
    public async Task Preview_names_the_registry_values_it_will_write()
    {
        var tool = new HiddenFilesTool(new FakeExplorerSettings { Hidden = 2, HideFileExt = 1 });

        var preview = await tool.InspectAsync(default);

        Assert.Contains(preview.Items, i => i.Detail!.Contains("Hidden = 2 → 1", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Detail!.Contains("HideFileExt = 1 → 0", StringComparison.Ordinal));
    }
}

public class WindowsFlushDnsToolTests
{
    [Fact]
    public void Builds_the_expected_command()
    {
        Assert.Equal("ipconfig /flushdns", FlushDnsTool.Command.Display);
    }

    [Fact]
    public async Task Inspect_blocks_when_ipconfig_is_missing()
    {
        var preview = await new FlushDnsTool(new FakeRunner()).InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    [Fact]
    public async Task Run_invokes_ipconfig_once()
    {
        var runner = new FakeRunner().WithBinary("ipconfig");
        var tool = new FlushDnsTool(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "ipconfig /flushdns" }, runner.RanDisplays);
    }

    [Fact]
    public async Task A_non_zero_exit_is_reported_as_a_failure()
    {
        var runner = new FakeRunner().WithBinary("ipconfig")
            .Returns("ipconfig /flushdns", 1, stderr: "Could not flush the DNS Resolver Cache");
        var tool = new FlushDnsTool(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains("Resolver Cache", StringComparison.Ordinal));
    }
}
