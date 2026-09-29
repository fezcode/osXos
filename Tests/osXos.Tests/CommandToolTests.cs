using OsXos.Tools;
using OsXos.Tools.Linux;
using OsXos.Tools.MacOS;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The toggle, report and command tools: Windows against an in-memory registry,
/// macOS and Linux against a scripted runner. Nothing here changes the machine.
/// </summary>
public class CommandToolTests
{
    static async Task<ToolResult> InspectAndRun(ITool tool) =>
        await tool.RunAsync(await tool.InspectAsync(default), default);

    // --------------------------------------------------- classic context menu --

    [Fact]
    public async Task Classic_menu_is_blocked_on_windows_10()
    {
        var tool = new ClassicContextMenuTool(new FakeUserRegistry(), new FakeShellController(), () => 19045);
        Assert.False((await tool.InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Classic_menu_creates_the_key_restarts_explorer_and_undoes_itself()
    {
        var registry = new FakeUserRegistry();
        var shell = new FakeShellController();
        var tool = new ClassicContextMenuTool(registry, shell, () => 26100);

        var on = await InspectAndRun(tool);
        Assert.True(on.Ok);
        Assert.True(registry.KeyExists(ClassicContextMenuTool.ServerKey));
        Assert.Equal(1, shell.StopCount);
        Assert.Equal(1, shell.StartCount);

        await InspectAndRun(tool);
        Assert.False(registry.KeyExists(ClassicContextMenuTool.ServerKey));
        Assert.False(registry.KeyExists(ClassicContextMenuTool.ClsidKey));
    }

    [Fact]
    public void Classic_menu_only_ever_touches_its_own_clsid()
    {
        Assert.StartsWith(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}",
            ClassicContextMenuTool.ServerKey, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- explorer toggles --

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Explorer_toggles_flip_their_value_and_tell_windows(int? before, int after)
    {
        var registry = new FakeUserRegistry();
        var toggle = ExplorerToggleTool.ClockSeconds;
        if (before is { } b) registry.SetDword(toggle.KeyPath, toggle.ValueName, b);

        var result = await InspectAndRun(new ExplorerToggleTool(toggle, registry));

        Assert.True(result.Ok);
        Assert.Equal(after, registry.GetDword(toggle.KeyPath, toggle.ValueName));
        Assert.Equal(new[] { "TraySettings" }, registry.Broadcasts);
    }

    [Fact]
    public async Task Full_path_toggle_writes_cabinet_state()
    {
        var registry = new FakeUserRegistry();
        await InspectAndRun(new ExplorerToggleTool(ExplorerToggleTool.FullPathTitle, registry));

        Assert.Equal(1, registry.GetDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath"));
    }

    [Fact]
    public async Task Explorer_toggle_inspect_changes_nothing()
    {
        var registry = new FakeUserRegistry();
        await new ExplorerToggleTool(ExplorerToggleTool.ClockSeconds, registry).InspectAsync(default);

        Assert.Empty(registry.Dwords);
        Assert.Empty(registry.Broadcasts);
    }

    // ------------------------------------------------------------ startup apps --

    [Theory]
    [InlineData(new byte[] { 2, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 7, 0, 0, 0 }, false)]
    public void Startup_approval_bytes_decode_the_way_task_manager_writes_them(byte[] value, bool enabled)
    {
        Assert.Equal(enabled, StartupAppsTool.IsEnabled(value));
    }

    [Fact]
    public async Task Startup_report_lists_every_source_with_its_state_and_changes_nothing()
    {
        using var dir = new TempDir();
        var registry = new FakeRegistry()
            .Set(RegHive.CurrentUser, StartupAppsTool.RunKey, "Discord", @"C:\discord.exe --start-minimized")
            .Set(RegHive.CurrentUser, StartupAppsTool.RunKey, "OneDrive", @"C:\onedrive.exe /background")
            .Set(RegHive.CurrentUser, StartupAppsTool.ApprovedRun, "OneDrive", new byte[] { 3, 0, 0, 0 })
            .Set(RegHive.LocalMachine, StartupAppsTool.RunKey, "SecurityHealth", @"C:\Windows\health.exe");
        var userStartup = dir.Dir("user-startup");
        dir.File(Path.Combine("user-startup", "Notes.lnk"));
        dir.File(Path.Combine("user-startup", "desktop.ini"));

        var tool = new StartupAppsTool(registry, userStartup, dir.Sub("no-common"));
        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.Equal(4, preview.Items.Count);
        Assert.Contains(preview.Items, i => i.Label == "Discord  (enabled)");
        Assert.Contains(preview.Items, i => i.Label == "OneDrive  (disabled)");
        Assert.Contains(preview.Items, i => i.Label == "SecurityHealth  (enabled)" && i.Detail!.StartsWith("everyone", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Label == "Notes  (enabled)");
        Assert.Contains("3 enabled", preview.Summary, StringComparison.Ordinal);
        Assert.True(result.Ok);
        Assert.Empty(registry.Deleted);
    }

    // ------------------------------------------------------------------- macOS --

    [Fact]
    public async Task Simulators_lists_only_the_unavailable_ones_then_uses_simctl_to_delete()
    {
        const string listing = """
            == Devices ==
            -- iOS 15.0 --
                iPhone 8 (A1B2) (Shutdown) (unavailable, runtime profile not found)
                iPad Air (C3D4) (Shutdown) (unavailable, runtime profile not found)
            """;
        var runner = new FakeRunner().WithBinary("xcrun")
            .Returns(UnavailableSimulatorsTool.ListCommand.Display, 0, listing);
        var tool = new UnavailableSimulatorsTool(runner);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.Contains("2 unavailable simulators", preview.Summary, StringComparison.Ordinal);
        Assert.Contains(preview.Items, i => i.Label == "iPhone 8");
        Assert.True(result.Ok);
        Assert.Contains("xcrun simctl delete unavailable", runner.RanDisplays);
    }

    [Fact]
    public async Task Simulators_with_nothing_dead_blocks()
    {
        var runner = new FakeRunner().WithBinary("xcrun")
            .Returns(UnavailableSimulatorsTool.ListCommand.Display, 0, "== Devices ==\n");
        Assert.False((await new UnavailableSimulatorsTool(runner).InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Mac_trash_blocks_when_empty_and_explains_a_declined_automation_prompt()
    {
        var empty = new FakeRunner().WithBinary("osascript")
            .Returns(MacEmptyTrashTool.CountCommand.Display, 0, "0\n");
        Assert.False((await new MacEmptyTrashTool(empty).InspectAsync(default)).CanRun);

        var refused = new FakeRunner().WithBinary("osascript")
            .Returns(MacEmptyTrashTool.CountCommand.Display, 1, stderr: "Not authorized to send Apple events to Finder. (-1743)");
        var preview = await new MacEmptyTrashTool(refused).InspectAsync(default);
        Assert.False(preview.CanRun);
        Assert.Contains("Automation", preview.Blocker, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mac_trash_empties_through_finder()
    {
        var runner = new FakeRunner().WithBinary("osascript")
            .Returns(MacEmptyTrashTool.CountCommand.Display, 0, "12\n");
        var tool = new MacEmptyTrashTool(runner);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.Contains("12 items", preview.Summary, StringComparison.Ordinal);
        Assert.True(result.Ok);
        Assert.Contains(MacEmptyTrashTool.EmptyCommand.Display, runner.RanDisplays);
    }

    [Fact]
    public async Task Finder_bars_turn_both_on_unless_both_already_are()
    {
        var partly = new FakeRunner().WithBinary("defaults")
            .Returns(FinderBarsTool.Read("ShowPathbar").Display, 0, "1\n")
            .Returns(FinderBarsTool.Read("ShowStatusBar").Display, 1, stderr: "does not exist");
        await InspectAndRun(new FinderBarsTool(partly));
        Assert.Contains(FinderBarsTool.Write("ShowPathbar", true).Display, partly.RanDisplays);
        Assert.Contains(FinderBarsTool.Write("ShowStatusBar", true).Display, partly.RanDisplays);
        Assert.Contains("killall Finder", partly.RanDisplays);

        var both = new FakeRunner().WithBinary("defaults")
            .Returns(FinderBarsTool.Read("ShowPathbar").Display, 0, "1\n")
            .Returns(FinderBarsTool.Read("ShowStatusBar").Display, 0, "1\n");
        await InspectAndRun(new FinderBarsTool(both));
        Assert.Contains(FinderBarsTool.Write("ShowPathbar", false).Display, both.RanDisplays);
    }

    [Fact]
    public async Task Screenshots_move_to_pictures_creating_the_folder_then_back_to_the_desktop()
    {
        using var dir = new TempDir();
        var home = dir.Path;
        var folder = Path.Combine(home, "Pictures", "Screenshots");

        var fresh = new FakeRunner().WithBinary("defaults")
            .Returns(ScreenshotFolderTool.ReadCommand.Display, 1, stderr: "does not exist");
        var result = await InspectAndRun(new ScreenshotFolderTool(fresh, home));
        Assert.True(result.Ok);
        Assert.True(Directory.Exists(folder));
        Assert.Contains(ScreenshotFolderTool.WriteCommand(folder).Display, fresh.RanDisplays);
        Assert.Contains("killall SystemUIServer", fresh.RanDisplays);

        var moved = new FakeRunner().WithBinary("defaults")
            .Returns(ScreenshotFolderTool.ReadCommand.Display, 0, folder + "\n");
        await InspectAndRun(new ScreenshotFolderTool(moved, home));
        Assert.Contains(ScreenshotFolderTool.ResetCommand.Display, moved.RanDisplays);
    }

    [Fact]
    public async Task Dock_and_quick_look_run_their_one_command()
    {
        var runner = new FakeRunner().WithBinary("qlmanage");
        Assert.True((await InspectAndRun(new RestartDockTool(runner))).Ok);
        Assert.True((await InspectAndRun(new QuickLookCacheTool(runner))).Ok);
        Assert.Equal(new[] { "killall Dock", "qlmanage -r cache" }, runner.RanDisplays);
    }

    // ------------------------------------------------------------------- Linux --

    [Fact]
    public async Task Flatpak_removes_unused_from_the_user_installation_only()
    {
        var runner = new FakeRunner().WithBinary("flatpak")
            .Returns(FlatpakUnusedTool.ListUserRuntimes.Display, 0, "org.gnome.Platform\t45\norg.kde.Platform\t6.6\n");
        var tool = new FlatpakUnusedTool(runner);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.Contains("2 runtimes", preview.Summary, StringComparison.Ordinal);
        Assert.True(result.Ok);
        Assert.Contains("flatpak uninstall --user --unused --noninteractive -y", runner.RanDisplays);
        Assert.DoesNotContain(runner.RanDisplays, d => d.Contains("--system", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Flatpak_blocks_without_flatpak()
    {
        Assert.False((await new FlatpakUnusedTool(new FakeRunner()).InspectAsync(default)).CanRun);
    }

    sealed class FakeElevation : IElevationService
    {
        public List<ShellCommand> Ran { get; } = new();
        public int ExitCode { get; set; }
        public bool IsElevated => false;
        public bool CanElevate { get; set; } = true;
        public string PromptDescription => "fake";

        public Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct)
        {
            Ran.Add(command);
            return Task.FromResult(new ProcessOutcome(ExitCode, "", ""));
        }
    }

    [Theory]
    [InlineData("apt-get", "apt-get clean")]
    [InlineData("dnf", "dnf clean packages")]
    [InlineData("pacman", "pacman -Sc --noconfirm")]
    [InlineData("zypper", "zypper --non-interactive clean --all")]
    public async Task Package_cache_uses_the_managers_own_command_elevated(string binary, string expected)
    {
        var elevation = new FakeElevation();
        var tool = new PackageCacheTool(new FakeRunner().WithBinary(binary), elevation, _ => 1024);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.True(preview.NeedsElevation);
        Assert.True(((ITool)tool).RequiresElevation);
        Assert.True(result.Ok);
        Assert.Equal(expected, Assert.Single(elevation.Ran).Display);
    }

    [Fact]
    public async Task Package_cache_reports_a_declined_prompt_as_cancelled()
    {
        var elevation = new FakeElevation { ExitCode = 126 };
        var tool = new PackageCacheTool(new FakeRunner().WithBinary("apt-get"), elevation, _ => 1024);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Equal("Cancelled", result.Headline);
    }

    [Fact]
    public async Task Package_cache_without_pkexec_shows_the_sudo_command()
    {
        var elevation = new FakeElevation { CanElevate = false };
        var tool = new PackageCacheTool(new FakeRunner().WithBinary("apt-get"), elevation, _ => 1024);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Contains("sudo apt-get clean", result.Lines);
        Assert.Empty(elevation.Ran);
    }

    [Fact]
    public async Task Restart_audio_restarts_only_what_is_running()
    {
        var runner = new FakeRunner { Default = new ProcessOutcome(3, "inactive\n", "") }.WithBinary("systemctl")
            .Returns(RestartAudioTool.IsActive("pipewire.service").Display, 0, "active\n")
            .Returns(RestartAudioTool.IsActive("wireplumber.service").Display, 0, "active\n")
            .Returns(RestartAudioTool.Restart(new[] { "pipewire.service", "wireplumber.service" }).Display, 0);
        var tool = new RestartAudioTool(runner);

        var result = await InspectAndRun(tool);

        Assert.True(result.Ok);
        Assert.Contains("systemctl --user restart pipewire.service wireplumber.service", runner.RanDisplays);
        Assert.DoesNotContain(runner.RanDisplays, d => d.Contains("restart", StringComparison.Ordinal) && d.Contains("pulseaudio", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restart_audio_blocks_when_no_sound_server_runs()
    {
        var runner = new FakeRunner { Default = new ProcessOutcome(3, "inactive\n", "") }.WithBinary("systemctl");
        Assert.False((await new RestartAudioTool(runner).InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Failed_services_lists_both_scopes_and_changes_nothing()
    {
        var runner = new FakeRunner().WithBinary("systemctl")
            .Returns(FailedServicesTool.UserFailed.Display, 0, "syncthing.service loaded failed failed Syncthing - Open Source Continuous File Synchronization\n")
            .Returns(FailedServicesTool.SystemFailed.Display, 0, "");
        var tool = new FailedServicesTool(runner);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        var row = Assert.Single(preview.Items);
        Assert.Equal("syncthing.service  (your session)", row.Label);
        Assert.StartsWith("Syncthing", row.Detail, StringComparison.Ordinal);
        Assert.True(result.Ok);
        Assert.Equal(2, runner.Ran.Count);
    }

    [Fact]
    public async Task Failed_services_with_nothing_failed_says_so()
    {
        var runner = new FakeRunner().WithBinary("systemctl");
        Assert.False((await new FailedServicesTool(runner).InspectAsync(default)).CanRun);
    }
}
