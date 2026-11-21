using OsXos.Tools;
using Xunit;

namespace OsXos.Tests;

public class CommandReportTests
{
    public static TheoryData<string, string, string[]> Reports => new()
    {
        { "windows.network-configuration", "ipconfig.exe", new[] { "/all" } },
        { "windows.network-connections", "netstat.exe", new[] { "-a", "-n", "-o" } },
        { "windows.sleep-states", "powercfg.exe", new[] { "/availablesleepstates" } },
        { "windows.installed-drivers", "driverquery.exe", new[] { "/fo", "list" } },
        { "macos.network-interfaces", "/sbin/ifconfig", new[] { "-a" } },
        { "macos.dns-configuration", "/usr/sbin/scutil", new[] { "--dns" } },
        { "macos.battery-status", "/usr/bin/pmset", new[] { "-g", "batt" } },
        { "macos.sleep-assertions", "/usr/bin/pmset", new[] { "-g", "assertions" } },
        { "linux.network-interfaces", "ip", new[] { "address", "show" } },
        { "linux.network-connections", "ss", new[] { "-t", "-u", "-a", "-n", "-p" } },
        { "linux.disk-space", "df", new[] { "-h", "-l" } },
        { "linux.memory-usage", "free", new[] { "-h" } },
    };

    public static IEnumerable<object[]> ReportFiles => Reports.Select(row => new[] { row[0], row[1] });

    static ITool Report(string id, IProcessRunner runner) =>
        (id.Split('.')[0] switch
        {
            "macos" => ToolCatalog.MacOS(runner),
            "linux" => ToolCatalog.Linux(runner),
            _ => TestCatalog.Windows(runner),
        }).Single(t => t.Id == id);

    static string CommandFile(string file) => file.EndsWith(".exe", StringComparison.Ordinal)
        ? Path.Combine(Environment.SystemDirectory, file) : file;

    static FakeRunner Runner(string file, ProcessOutcome outcome) =>
        new FakeRunner { Default = outcome }.WithBinary(CommandFile(file));

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Inspection_uses_only_the_local_read_command_and_result_keeps_the_shown_snapshot(
        string id, string file, string[] args)
    {
        var runner = Runner(file, new(0, "Windows report\r\n\r\n  État : disponible\r\n    Detail\r\n", ""));
        var tool = Report(id, runner);
        var preview = await tool.InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.False(preview.NeedsElevation);
        var command = Assert.Single(runner.Ran);
        Assert.Equal(CommandFile(file), command.File);
        Assert.Equal(args, command.Args);
        Assert.Equal(new[] { "Windows report", "  État : disponible", "    Detail" }, preview.Items.Select(i => i.Label));

        runner.Default = new(0, "Changed since inspection", "");
        var result = await tool.RunAsync(preview, default);

        Assert.True(result.Ok);
        Assert.Equal(preview.Items.Select(i => i.Label), result.Lines);
        Assert.Single(runner.Ran);
    }

    [Theory]
    [MemberData(nameof(ReportFiles))]
    public async Task Missing_binary_blocks_without_starting_a_process(string id, string file)
    {
        var runner = new FakeRunner();
        var tool = Report(id, runner);
        var preview = await tool.InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.Contains(Path.GetFileName(file), preview.Blocker);
        Assert.False((await tool.RunAsync(preview, default)).Ok);
        Assert.Empty(runner.Ran);
    }

    [Theory]
    [MemberData(nameof(ReportFiles))]
    public async Task Failure_keeps_both_partial_output_and_the_error(string id, string file)
    {
        var runner = Runner(file, new(5, "Partial report", "Access denied"));
        var tool = Report(id, runner);
        var preview = await tool.InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.Contains("exit code 5", preview.Blocker);
        Assert.Contains("Partial report", preview.Blocker);
        Assert.Contains("Access denied", preview.Blocker);
        Assert.False((await tool.RunAsync(preview, default)).Ok);
        Assert.Single(runner.Ran);
    }

    [Theory]
    [MemberData(nameof(ReportFiles))]
    public async Task Empty_output_is_not_a_successful_report(string id, string file)
    {
        var preview = await Report(id, Runner(file, new(0, "\r\n  ", ""))).InspectAsync(default);
        Assert.False(preview.CanRun);
        Assert.Contains("no report", preview.Blocker);
    }

    [Theory]
    [MemberData(nameof(ReportFiles))]
    public async Task Diagnostic_messages_are_preserved_in_review_and_result(string id, string file)
    {
        var tool = Report(id, Runner(file, new(0, "Report", "Some entries unavailable\r\nDetails")));
        var preview = await tool.InspectAsync(default);
        Assert.True(preview.CanRun);
        Assert.Equal(3, preview.Items.Count);
        Assert.Equal("Diagnostic message", preview.Items[1].Detail);
        Assert.Contains("Some entries unavailable — Diagnostic message", (await tool.RunAsync(preview, default)).Lines);
    }

    [Fact]
    public async Task Large_inventories_are_not_truncated()
    {
        var lines = Enumerable.Range(0, 2000).Select(i => $"Module {i}").ToArray();
        var tool = Report("windows.installed-drivers", Runner("driverquery.exe", new(0, string.Join('\n', lines), "")));
        var preview = await tool.InspectAsync(default);
        Assert.Equal(lines, preview.Items.Select(i => i.Label));
    }

    [Fact]
    public async Task Cancellation_returned_as_a_process_error_still_cancels_the_inspection()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new CancellingRunner(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Report("windows.sleep-states", runner).InspectAsync(cancellation.Token));
    }

    sealed class CancellingRunner(CancellationTokenSource cancellation) : IProcessRunner
    {
        public bool Exists(string file) => true;
        public Task<ProcessOutcome> RunAsync(ShellCommand command, CancellationToken ct)
        {
            cancellation.Cancel();
            return Task.FromResult(new ProcessOutcome(-1, "", "The operation was canceled."));
        }
    }
}
