using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The elevation plumbing. Two tools use it, so what is worth pinning down is which
/// ones, and the shape of the commands it builds — getting a quoting rule wrong here means
/// handing a shell something other than what the UI showed the user.
/// </summary>
public class ElevationTests
{
    /// <summary>
    /// Exactly which tools may ask for administrator rights. Deliberately a list and
    /// not a count: a tool quietly gaining elevation is the thing worth catching, and
    /// adding one here is a small deliberate act that comes with updating the README.
    /// </summary>
    static readonly string[] MayElevate = { "windows.update-cache", "linux.package-cache", "windows.ai-sandbox-accounts" };

    [Fact]
    public void Only_the_tools_on_the_list_ask_for_elevation()
    {
        var runner = new FakeRunner();
        var all = TestCatalog.Windows()
            .Concat(ToolCatalog.MacOS(runner))
            .Concat(ToolCatalog.Linux(runner))
            .ToList();

        // osXos's own tools: exactly the list. WinUtil's entries run as administrator
        // because WinUtil does — each is marked as WinUtil's and says so on Review —
        // and a registry tweak only when one of its values is machine-wide.
        var own = all.Where(t => t.RequiresElevation && !FromWinUtil(t)).Select(t => t.Id).OrderBy(x => x).ToList();
        Assert.Equal(MayElevate.OrderBy(x => x), own);

        Assert.All(all.OfType<RegistryTweakTool>(), t =>
            Assert.Equal(t.Tweak.MachineWide, ((ITool)t).RequiresElevation));
        Assert.All(all.OfType<WinUtilScriptTool>(), t =>
            Assert.Equal(t.Entry.Kind != ScriptKind.Launcher, ((ITool)t).RequiresElevation));
    }

    static bool FromWinUtil(ITool tool) => tool switch
    {
        RegistryTweakTool t => t.Tweak.Source is not null,
        WinUtilScriptTool t => t.Entry.Source is not null,
        _ => false,
    };

    [Fact]
    public void Every_elevated_winutil_entry_says_it_needs_administrator_on_review()
    {
        var elevated = TestCatalog.Windows().Where(t => t.RequiresElevation && FromWinUtil(t)).ToList();
        Assert.NotEmpty(elevated);
        Assert.All(elevated, t => Assert.Contains(t.Steps, s =>
            s.Detail.Contains("administrator", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void A_tool_that_needs_elevation_says_so_on_its_preview_too()
    {
        // RequiresElevation drives the notice before the scan; the preview has to
        // carry it as well, or the notice vanishes the moment Review arrives.
        using var dir = new TempDir();
        dir.File("update.cab", 4096);

        var tool = new UpdateCacheTool(dir.Path, new ElevationService(new FakeRunner()));
        var preview = tool.InspectAsync(default).Result;

        Assert.True(tool.RequiresElevation);
        Assert.True(preview.NeedsElevation);
    }

    [Fact]
    public void A_preview_does_not_ask_for_elevation_unless_told_to()
    {
        Assert.False(new ToolPreview(Array.Empty<PreviewItem>(), "").NeedsElevation);
        Assert.True(new ToolPreview(Array.Empty<PreviewItem>(), "") { NeedsElevation = true }.NeedsElevation);
    }

    [Fact]
    public void Pkexec_keeps_the_argument_vector_intact()
    {
        // polkit execs the program directly, so nothing needs quoting and nothing
        // may be collapsed into a single string.
        var wrapped = ElevationService.PkexecFor(
            new ShellCommand("gtk-update-icon-cache", "-f", "-t", "/usr/share/icons/Papirus"));

        Assert.Equal("pkexec", wrapped.File);
        Assert.Equal(
            new[] { "gtk-update-icon-cache", "-f", "-t", "/usr/share/icons/Papirus" },
            wrapped.Args);
    }

    [Fact]
    public void AppleScript_wraps_the_command_for_the_native_prompt()
    {
        var wrapped = ElevationService.AppleScriptFor(new ShellCommand("killall", "Dock"));

        Assert.Equal("osascript", wrapped.File);
        Assert.Equal("-e", wrapped.Args[0]);
        Assert.Equal("do shell script \"killall Dock\" with administrator privileges", wrapped.Args[1]);
    }

    [Theory]
    // The text lands inside an AppleScript string literal that a shell then parses,
    // so both backslashes and quotes have to survive being read twice.
    [InlineData("rm -rf /tmp/a", "rm -rf /tmp/a")]
    [InlineData("say \"hi\"", "say \\\"hi\\\"")]
    [InlineData("C:\\path", "C:\\\\path")]
    [InlineData("a\\\"b", "a\\\\\\\"b")]
    public void AppleScript_escaping_survives_both_readers(string input, string expected)
    {
        Assert.Equal(expected, ElevationService.EscapeForAppleScript(input));
    }

    [Fact]
    public void A_quoted_path_reaches_AppleScript_intact()
    {
        var wrapped = ElevationService.AppleScriptFor(
            new ShellCommand("rm", "-rf", "/Library/Caches/my app"));

        // ShellCommand.Display quotes the spaced argument; the escaping then has to
        // protect those quotes rather than let AppleScript end the string early.
        Assert.Equal(
            "do shell script \"rm -rf \\\"/Library/Caches/my app\\\"\" with administrator privileges",
            wrapped.Args[1]);
    }

    [Fact]
    public void Elevation_is_considered_available_on_this_machine()
    {
        // Windows and macOS always have a mechanism; Linux needs pkexec present.
        var service = new ElevationService(new FakeRunner());
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), service.CanElevate);
    }

    [Fact]
    public void The_prompt_is_described_before_anything_is_attempted()
    {
        // The UI shows this text next to the Run button, so it must never be empty.
        var service = new ElevationService(new FakeRunner());
        Assert.False(string.IsNullOrWhiteSpace(service.PromptDescription));
    }

    [Fact]
    public async Task An_unelevatable_system_reports_that_rather_than_throwing()
    {
        // A FakeRunner knows no binaries, so on Linux this is the no-pkexec path.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return;

        var outcome = await new ElevationService(new FakeRunner())
            .RunElevatedAsync(new ShellCommand("true"), default);

        Assert.False(outcome.Ok);
    }
}
