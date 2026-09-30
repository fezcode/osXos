using System.Text;
using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The registry tweak engine and the tweaks built on it. Nothing here writes to the
/// real registry: <c>reg import</c> is played by a runner that applies the .reg file
/// to an in-memory registry, and elevation by a stand-in that does the same.
/// </summary>
public class RegistryTweakTests
{
    /// <summary>Applies a .reg file osXos wrote to a <see cref="FakeRegistry"/>, the way reg import would.</summary>
    static void Import(string file, FakeRegistry registry)
    {
        var text = File.ReadAllText(file, Encoding.Unicode);
        Assert.StartsWith("Windows Registry Editor Version 5.00", text.TrimStart('\uFEFF'), StringComparison.Ordinal);

        RegHive hive = RegHive.CurrentUser;
        var key = "";
        foreach (var raw in text.Split("\r\n"))
        {
            var line = raw.Trim('\uFEFF');
            if (line.StartsWith('['))
            {
                var full = line[1..^1];
                var cut = full.IndexOf('\\');
                hive = full[..cut] switch
                {
                    "HKEY_LOCAL_MACHINE" => RegHive.LocalMachine,
                    "HKEY_USERS" => RegHive.Users,
                    _ => RegHive.CurrentUser,
                };
                key = full[(cut + 1)..];
                continue;
            }
            if (!line.StartsWith('"')) continue;

            var eq = line.IndexOf("\"=", StringComparison.Ordinal);
            var name = line[1..eq].Replace("\\\"", "\"").Replace("\\\\", "\\");
            var data = line[(eq + 2)..];

            if (data == "-") registry.DeleteValue(hive, key, name);
            else if (data.StartsWith("dword:", StringComparison.Ordinal))
                registry.Set(hive, key, name, unchecked((int)Convert.ToUInt32(data[6..], 16)));
            else if (data.StartsWith("hex(b):", StringComparison.Ordinal))
                registry.Set(hive, key, name, BitConverter.ToInt64(data[7..].Split(',').Select(b => Convert.ToByte(b, 16)).ToArray()));
            else
                registry.Set(hive, key, name, data[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\"));
        }
    }

    /// <summary>A runner whose reg import edits the fake registry.</summary>
    sealed class ImportingRunner : IProcessRunner
    {
        readonly FakeRegistry _registry;
        public List<ShellCommand> Ran { get; } = new();
        public ImportingRunner(FakeRegistry registry) => _registry = registry;

        public Task<ProcessOutcome> RunAsync(ShellCommand command, CancellationToken ct)
        {
            Ran.Add(command);
            if (command.File == "reg.exe" && command.Args[0] == "import") Import(command.Args[1], _registry);
            return Task.FromResult(new ProcessOutcome(0, "", "The operation completed successfully."));
        }

        public bool Exists(string file) => true;
    }

    sealed class ImportingElevation : IElevationService
    {
        readonly FakeRegistry _registry;
        public List<ShellCommand> Ran { get; } = new();
        public int ExitCode { get; set; }
        public ImportingElevation(FakeRegistry registry) => _registry = registry;
        public bool IsElevated => false;
        public bool CanElevate => true;
        public string PromptDescription => "fake";

        public Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct)
        {
            Ran.Add(command);
            if (ExitCode == 0) Import(command.Args[1], _registry);
            return Task.FromResult(new ProcessOutcome(ExitCode, "", ""));
        }
    }

    sealed record Rig(RegistryTweakTool Tool, FakeRegistry Registry, ImportingRunner Runner,
        ImportingElevation Elevation, FakeShellController Shell, FakeUserRegistry Broadcast);

    static Rig Build(RegistryTweak tweak, FakeRegistry? registry = null)
    {
        registry ??= new FakeRegistry();
        var runner = new ImportingRunner(registry);
        var elevation = new ImportingElevation(registry);
        var shell = new FakeShellController();
        var broadcast = new FakeUserRegistry();
        var tool = new RegistryTweakTool(tweak, registry, broadcast, shell, runner, elevation,
            () => Path.Combine(Path.GetTempPath(), $"osxos-test-{Guid.NewGuid():N}.reg"));
        return new Rig(tool, registry, runner, elevation, shell, broadcast);
    }

    static async Task<ToolResult> InspectAndRun(ITool tool) =>
        await tool.RunAsync(await tool.InspectAsync(default), default);

    static RegistryTweak Sample(params TweakValue[] values) => new(
        "tweak-sample", ToolCategory.System, "Disable Sample", "sample", "IconSystem", values,
        RestartsExplorer: false, AppliedText: "on.", RevertedText: "off.",
        Steps: new[] { new ToolStep("a", "b"), new ToolStep("c", "d") });

    // ------------------------------------------------------------- the engine --

    [Fact]
    public async Task A_user_tweak_imports_without_elevation_and_undoes_to_the_originals()
    {
        var tweak = Sample(
            new TweakValue(RegHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", RegValueKind.DWord, "1", "0", "speed"),
            new TweakValue(RegHive.CurrentUser, @"Software\Policies\X", "Off", RegValueKind.DWord, "1", null, "policy"));
        var rig = Build(tweak);

        var on = await InspectAndRun(rig.Tool);
        Assert.True(on.Ok);
        Assert.Equal(1, rig.Registry.GetValue(RegHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed"));
        Assert.Equal(1, rig.Registry.GetValue(RegHive.CurrentUser, @"Software\Policies\X", "Off"));
        Assert.Single(rig.Runner.Ran);
        Assert.Empty(rig.Elevation.Ran);
        Assert.False(((ITool)rig.Tool).RequiresElevation);

        var off = await InspectAndRun(rig.Tool);
        Assert.True(off.Ok);
        Assert.Equal(0, rig.Registry.GetValue(RegHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed"));
        Assert.Null(rig.Registry.GetValue(RegHive.CurrentUser, @"Software\Policies\X", "Off"));
    }

    [Fact]
    public async Task A_machine_wide_tweak_elevates_one_import_and_says_so_up_front()
    {
        var tweak = Sample(new TweakValue(RegHive.LocalMachine, @"SOFTWARE\Policies\Y", "Z", RegValueKind.DWord, "1", null, "z"));
        var rig = Build(tweak);

        var preview = await rig.Tool.InspectAsync(default);
        var result = await rig.Tool.RunAsync(preview, default);

        Assert.True(((ITool)rig.Tool).RequiresElevation);
        Assert.True(preview.NeedsElevation);
        Assert.True(result.Ok);
        Assert.Single(rig.Elevation.Ran);
        Assert.Equal("reg.exe", rig.Elevation.Ran[0].File);
        Assert.Empty(rig.Runner.Ran);
        Assert.Equal(1, rig.Registry.GetValue(RegHive.LocalMachine, @"SOFTWARE\Policies\Y", "Z"));
    }

    [Fact]
    public async Task A_declined_prompt_is_cancelled_and_changes_nothing()
    {
        var tweak = Sample(new TweakValue(RegHive.LocalMachine, @"SOFTWARE\Y", "Z", RegValueKind.DWord, "1", null, "z"));
        var rig = Build(tweak);
        rig.Elevation.ExitCode = 1223;

        var result = await InspectAndRun(rig.Tool);

        Assert.False(result.Ok);
        Assert.Equal("Cancelled", result.Headline);
        Assert.Null(rig.Registry.GetValue(RegHive.LocalMachine, @"SOFTWARE\Y", "Z"));
    }

    [Fact]
    public async Task The_result_reports_what_the_registry_holds_not_what_was_asked()
    {
        // The import "succeeds" but the value never lands — a policy key locked by
        // an administrator looks exactly like this.
        var registry = new FakeRegistry();
        var tweak = Sample(new TweakValue(RegHive.CurrentUser, @"Software\Locked", "V", RegValueKind.DWord, "1", null, "v"));
        var shell = new FakeShellController();
        var tool = new RegistryTweakTool(tweak, registry, new FakeUserRegistry(), shell,
            new FakeRunner(), new ImportingElevation(registry));

        var result = await InspectAndRun(tool);

        Assert.False(result.Ok);
        Assert.Contains(result.Lines, l => l.Contains(@"HKEY_CURRENT_USER\Software\Locked\V is still not set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Strings_qwords_and_the_default_users_hive_round_trip()
    {
        var tweak = Sample(
            new TweakValue(RegHive.Users, @".DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators", RegValueKind.String, "2", "0", "numlock"),
            new TweakValue(RegHive.CurrentUser, @"Software\Q", "Big", RegValueKind.QWord, "5000000000", null, "q"));
        var rig = Build(tweak);

        Assert.True((await InspectAndRun(rig.Tool)).Ok);
        Assert.Equal("2", rig.Registry.GetValue(RegHive.Users, @".DEFAULT\Control Panel\Keyboard", "InitialKeyboardIndicators"));
        Assert.Equal(5000000000L, rig.Registry.GetValue(RegHive.CurrentUser, @"Software\Q", "Big"));
    }

    [Fact]
    public void The_reg_file_escapes_names_and_spells_every_kind_the_way_regedit_does()
    {
        var text = RegistryTweakTool.RegFile(new (TweakValue, string?)[]
        {
            (new TweakValue(RegHive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", RegValueKind.DWord, "255", "0", ""), "255"),
            (new TweakValue(RegHive.CurrentUser, @"Control Panel\Mouse", "Gone", RegValueKind.DWord, "1", null, ""), null),
            (new TweakValue(RegHive.LocalMachine, @"SOFTWARE\S", "Say \"hi\"", RegValueKind.String, @"C:\x", null, ""), @"C:\x"),
            (new TweakValue(RegHive.LocalMachine, @"SOFTWARE\S", "Q", RegValueKind.QWord, "1", null, ""), "1"),
        });

        Assert.Contains("[HKEY_CURRENT_USER\\Control Panel\\Mouse]\r\n\"MouseSpeed\"=dword:000000ff\r\n\"Gone\"=-", text, StringComparison.Ordinal);
        Assert.Contains("\"Say \\\"hi\\\"\"=\"C:\\\\x\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Q\"=hex(b):01,00,00,00,00,00,00,00", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_changes_nothing_and_a_partly_applied_tweak_offers_to_finish()
    {
        var registry = new FakeRegistry()
            .Set(RegHive.CurrentUser, @"Software\P", "A", 1);
        var tweak = Sample(
            new TweakValue(RegHive.CurrentUser, @"Software\P", "A", RegValueKind.DWord, "1", null, "a"),
            new TweakValue(RegHive.CurrentUser, @"Software\P", "B", RegValueKind.DWord, "1", null, "b"));
        var rig = Build(tweak, registry);

        var preview = await rig.Tool.InspectAsync(default);

        Assert.StartsWith("Will apply it", preview.Summary, StringComparison.Ordinal);
        Assert.Contains(preview.Items, i => i.Detail!.Contains("already set", StringComparison.Ordinal));
        Assert.Empty(rig.Runner.Ran);
        Assert.Null(registry.GetValue(RegHive.CurrentUser, @"Software\P", "B"));
    }

    [Fact]
    public async Task Explorer_restarts_only_for_tweaks_that_need_it()
    {
        var rig = Build(OsXosTweaks.BingSearch);
        await InspectAndRun(rig.Tool);
        Assert.Equal(1, rig.Shell.StopCount);

        var quiet = Build(OsXosTweaks.AdvertisingId);
        await InspectAndRun(quiet.Tool);
        Assert.Equal(0, quiet.Shell.StopCount);
    }

    // ------------------------------------------------------------ the tweaks --

    static IEnumerable<RegistryTweak> Every => OsXosTweaks.All.Concat(WinUtilTweaks.All);

    [Fact]
    public void Every_tweak_is_well_formed()
    {
        var slugs = Every.Select(t => t.Slug).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct().Count());

        Assert.All(Every, t =>
        {
            Assert.NotEmpty(t.Values);
            Assert.True(t.Steps.Count >= 3, $"{t.Slug} explains too little");
            Assert.All(t.Values, v =>
            {
                Assert.False(v.KeyPath.StartsWith('\\'), $"{t.Slug}: {v.KeyPath}");
                Assert.DoesNotContain(':', v.KeyPath);
                Assert.False(string.IsNullOrWhiteSpace(v.Name), $"{t.Slug} has an unnamed value");
                if (v.Kind == RegValueKind.DWord)
                {
                    Assert.True(uint.TryParse(v.Value, out _), $"{t.Slug}/{v.Name}: '{v.Value}'");
                    if (v.Original is { } o) Assert.True(uint.TryParse(o, out _), $"{t.Slug}/{v.Name} original: '{o}'");
                }
            });
        });
    }

    [Fact]
    public void A_tweak_asks_for_admin_exactly_when_a_value_is_outside_the_users_own_hive()
    {
        var runner = new FakeRunner();
        var tools = TestCatalog.Windows(runner).OfType<RegistryTweakTool>().ToList();

        Assert.Equal(Every.Count(), tools.Count);
        Assert.All(tools, t => Assert.Equal(
            t.Tweak.Values.Any(v => v.Hive != RegHive.CurrentUser), ((ITool)t).RequiresElevation));
        Assert.All(OsXosTweaks.All, t => Assert.False(t.MachineWide, $"{t.Slug} is osXos's own and must stay per-user"));
    }

    [Fact]
    public void The_winutil_tweaks_credit_their_source()
    {
        Assert.All(WinUtilTweaks.All, t =>
        {
            Assert.Equal("Chris Titus Tech's WinUtil", t.Source);
            Assert.Contains(t.Steps, s => s.Detail.Contains("WinUtil", StringComparison.Ordinal));
        });
        Assert.All(OsXosTweaks.All, t => Assert.Null(t.Source));
    }

    [Fact]
    public void Tweaks_land_in_their_own_category_and_keep_the_catalogue_grouped()
    {
        var tools = TestCatalog.Windows();
        var order = CategoryCatalog.For(OSKind.Windows).Select(c => c.Category).ToList();
        var positions = tools.Select(t => order.IndexOf(t.Category)).ToList();

        Assert.Equal(positions.OrderBy(p => p), positions);
    }
}
