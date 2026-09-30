using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The WinUtil script engine. PowerShell never runs here: tests read the script it
/// would run, drive the state logic from an in-memory registry and a scripted
/// feature query, and stand in for elevation.
/// </summary>
public class WinUtilScriptTests
{
    sealed class RecordingElevation : IElevationService
    {
        public List<ShellCommand> Ran { get; } = new();
        public List<string> Scripts { get; } = new();
        public int ExitCode { get; set; }
        public bool IsElevated => false;
        public bool CanElevate => true;
        public string PromptDescription => "fake";

        public Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct)
        {
            Ran.Add(command);
            var file = command.Args[^1];
            if (File.Exists(file)) Scripts.Add(File.ReadAllText(file));
            return Task.FromResult(new ProcessOutcome(ExitCode, "", ""));
        }
    }

    static WinUtilScriptTool Tool(WinUtilScript entry, FakeRegistry? registry = null, FakeRunner? runner = null,
        RecordingElevation? elevation = null) =>
        new(entry, registry ?? new FakeRegistry(), runner ?? new FakeRunner(), elevation ?? new RecordingElevation());

    static WinUtilScript Entry(ScriptKind kind) => new("winutil-sample", ToolCategory.System, "Sample", "s", "IconSystem", kind,
        new[] { new ToolStep("a", "b"), new ToolStep("c", "needs administrator rights") });

    static async Task<ToolResult> InspectAndRun(ITool tool) => await tool.RunAsync(await tool.InspectAsync(default), default);

    // ---------------------------------------------------------------- scripts --

    [Fact]
    public void The_apply_script_sets_values_services_and_features_then_runs_winutils_script()
    {
        var entry = Entry(ScriptKind.Toggle) with
        {
            Registry = new[] { new TweakValue(RegHive.LocalMachine, @"SOFTWARE\Policies\X", "Off", RegValueKind.DWord, "1", null, "off") },
            Services = new[] { new ServiceChange("DiagTrack", "Disabled", "Automatic"), new ServiceChange("Late", "AutomaticDelayedStart", "Manual") },
            Script = "powercfg.exe /hibernate off",
            UndoScript = "powercfg.exe /hibernate on",
        };

        var apply = Tool(entry).BuildScript(undo: false, @"C:\t\x.log");
        var undo = Tool(entry).BuildScript(undo: true, @"C:\t\x.log");

        Assert.Contains("Set-ItemProperty -Path 'Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\X' -Name 'Off' -Type DWord -Value '1' -Force", apply, StringComparison.Ordinal);
        Assert.Contains("Set-Service -Name 'DiagTrack' -StartupType Disabled", apply, StringComparison.Ordinal);
        Assert.Contains("sc.exe config Late start= delayed-auto", apply, StringComparison.Ordinal);
        Assert.Contains("powercfg.exe /hibernate off", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("/hibernate on", apply, StringComparison.Ordinal);

        Assert.Contains("Remove-ItemProperty -Path 'Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\X' -Name 'Off'", undo, StringComparison.Ordinal);
        Assert.Contains("Set-Service -Name 'DiagTrack' -StartupType Automatic", undo, StringComparison.Ordinal);
        Assert.Contains("powercfg.exe /hibernate on", undo, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_script_carries_the_winutil_helper_stand_ins_and_a_transcript()
    {
        var script = Tool(Entry(ScriptKind.Action) with { Script = "Write-WinUtilLog -Message hi" }).BuildScript(false, @"C:\t\x.log");

        Assert.StartsWith("Start-Transcript -Path 'C:\\t\\x.log'", script, StringComparison.Ordinal);
        Assert.Contains("function Write-WinUtilLog", script, StringComparison.Ordinal);
        Assert.Contains("function Invoke-WinUtilExplorerUpdate", script, StringComparison.Ordinal);
        Assert.Contains("exit 1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Quotes_in_values_cannot_break_out_of_the_script()
    {
        var entry = Entry(ScriptKind.Toggle) with
        {
            Registry = new[] { new TweakValue(RegHive.CurrentUser, @"Software\It's", "Name'x", RegValueKind.String, "a'b", null, "q") },
        };
        var script = Tool(entry).BuildScript(false, "log");

        Assert.Contains("-Path 'Registry::HKEY_CURRENT_USER\\Software\\It''s' -Name 'Name''x' -Type String -Value 'a''b'", script, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ state --

    [Fact]
    public async Task A_toggle_reads_its_state_from_values_and_service_start_types()
    {
        var registry = new FakeRegistry()
            .Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2)
            .Set(RegHive.LocalMachine, @"SOFTWARE\Policies\X", "Off", 1);
        var entry = Entry(ScriptKind.Toggle) with
        {
            Registry = new[] { new TweakValue(RegHive.LocalMachine, @"SOFTWARE\Policies\X", "Off", RegValueKind.DWord, "1", null, "off") },
            Services = new[]
            {
                new ServiceChange("DiagTrack", "Disabled", "Automatic"),
                new ServiceChange("NotInstalledHere", "Disabled", "Manual"),
            },
            AppliedLabel = "Disabled",
            NotAppliedLabel = "Enabled",
        };
        var tool = Tool(entry, registry);

        var partly = await tool.ReadStateAsync(default);
        Assert.Equal("Partly applied", partly.Label);
        Assert.Contains("1 of 2", partly.Detail, StringComparison.Ordinal); // the missing service is not counted

        registry.Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4);
        Assert.Equal("Disabled", (await tool.ReadStateAsync(default)).Label);
    }

    [Fact]
    public async Task A_delayed_start_service_is_read_as_such()
    {
        var registry = new FakeRegistry()
            .Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\S", "Start", 2)
            .Set(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\S", "DelayedAutostart", 1);
        var entry = Entry(ScriptKind.Toggle) with { Services = new[] { new ServiceChange("S", "AutomaticDelayedStart", "Manual") } };

        Assert.Equal("Applied", (await Tool(entry, registry).ReadStateAsync(default)).Label);
    }

    [Fact]
    public async Task A_feature_asks_windows_and_is_blocked_once_enabled()
    {
        var entry = Entry(ScriptKind.Feature) with { Features = new[] { "Microsoft-Windows-Subsystem-Linux", "VirtualMachinePlatform" } };
        var query = WinUtilScriptTool.FeatureQuery(entry.Features).Display;

        var off = new FakeRunner().Returns(query, 0, "Microsoft-Windows-Subsystem-Linux=2\nVirtualMachinePlatform=2\n");
        Assert.Equal("Not enabled", (await Tool(entry, runner: off).ReadStateAsync(default)).Label);

        var half = new FakeRunner().Returns(query, 0, "Microsoft-Windows-Subsystem-Linux=1\nVirtualMachinePlatform=2\n");
        Assert.Equal("Partly enabled", (await Tool(entry, runner: half).ReadStateAsync(default)).Label);

        var on = new FakeRunner().Returns(query, 0, "Microsoft-Windows-Subsystem-Linux=1\nVirtualMachinePlatform=1\n");
        var preview = await Tool(entry, runner: on).InspectAsync(default);
        Assert.Equal("Enabled", preview.State!.Label);
        Assert.False(preview.CanRun);

        var absent = new FakeRunner().Returns(query, 0, "");
        Assert.Equal("Not on this PC", (await Tool(entry, runner: absent).ReadStateAsync(default)).Label);
    }

    // -------------------------------------------------------------------- run --

    [Fact]
    public async Task Running_elevates_one_powershell_file_and_cleans_it_up()
    {
        var elevation = new RecordingElevation();
        var tool = Tool(Entry(ScriptKind.Action) with { Script = "netsh winsock reset" }, elevation: elevation);

        var result = await InspectAndRun(tool);

        Assert.True(result.Ok);
        var command = Assert.Single(elevation.Ran);
        Assert.Equal("powershell.exe", command.File);
        Assert.Contains("-File", command.Args);
        Assert.Contains("netsh winsock reset", Assert.Single(elevation.Scripts), StringComparison.Ordinal);
        Assert.False(File.Exists(command.Args[^1]));
    }

    [Fact]
    public async Task A_declined_prompt_is_cancelled()
    {
        var elevation = new RecordingElevation { ExitCode = 1223 };
        var result = await InspectAndRun(Tool(Entry(ScriptKind.Action) with { Script = "x" }, elevation: elevation));

        Assert.False(result.Ok);
        Assert.Equal("Cancelled", result.Headline);
    }

    [Fact]
    public async Task A_toggle_whose_values_did_not_land_is_reported_as_not_finished()
    {
        // The elevated run "succeeds" but nothing changed — the result must say so.
        var entry = Entry(ScriptKind.Toggle) with
        {
            Registry = new[] { new TweakValue(RegHive.LocalMachine, @"SOFTWARE\X", "V", RegValueKind.DWord, "1", null, "v") },
        };
        var result = await InspectAndRun(Tool(entry));

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains("0 of 1 changes are in place", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_launcher_opens_its_panel_without_elevation()
    {
        var runner = new FakeRunner();
        var elevation = new RecordingElevation();
        var tool = Tool(Entry(ScriptKind.Launcher) with { Script = "cmd /c ncpa.cpl" }, runner: runner, elevation: elevation);

        var result = await InspectAndRun(tool);

        Assert.True(result.Ok);
        Assert.False(((ITool)tool).RequiresElevation);
        Assert.Empty(elevation.Ran);
        Assert.Contains(runner.Ran, c => c.Args.Contains("cmd /c ncpa.cpl"));
    }

    // ---------------------------------------------------------------- catalogue --

    [Fact]
    public void Every_generated_entry_is_well_formed_and_credits_winutil()
    {
        var slugs = WinUtilScripts.All.Select(e => e.Slug).Concat(WinUtilTweaks.All.Select(t => t.Slug)).Concat(OsXosTweaks.All.Select(t => t.Slug)).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct().Count());

        Assert.All(WinUtilScripts.All, e =>
        {
            Assert.Equal("Chris Titus Tech's WinUtil", e.Source);
            Assert.True(e.Steps.Count >= 3, e.Slug);
            Assert.Contains(e.Steps, s => s.Detail.Contains("WinUtil", StringComparison.Ordinal));
            if (e.Kind == ScriptKind.Toggle) Assert.True(e.Registry.Count + e.Services.Count > 0, $"{e.Slug} is a toggle with nothing to read");
            if (e.Kind == ScriptKind.Feature) Assert.NotEmpty(e.Features);
            if (e.Kind is ScriptKind.Action or ScriptKind.Launcher) Assert.False(string.IsNullOrWhiteSpace(e.Script), e.Slug);
        });
    }

    [Fact]
    public void Nothing_that_downloads_from_the_internet_was_taken()
    {
        // osXos makes no network requests. WinUtil's entries that fetch installers,
        // host lists or profiles are left out; this keeps one from slipping in.
        Assert.All(WinUtilScripts.All, e => Assert.DoesNotMatch(
            @"(?i)Invoke-WebRequest|Invoke-RestMethod|DownloadFile|Start-BitsTransfer|\birm\b|\biwr\b",
            (e.Script ?? "") + (e.UndoScript ?? "")));
    }

    [Fact]
    public void The_duplicates_of_osxos_tools_were_not_taken()
    {
        var names = WinUtilScripts.All.Select(e => e.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Dark Theme", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("Hidden Files", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("Right-Click Menu", StringComparison.Ordinal));
    }
}
