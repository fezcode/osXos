using OsXos.Tools;
using OsXos.Tools.Linux;
using OsXos.Tools.MacOS;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The macOS and Linux tools, tested for everything that does not need their OS:
/// the exact argv they build, how they read a command's output, and what they do
/// with a machine that cannot run them. Running them end to end needs a Mac and a
/// Linux box, and these tests do not pretend otherwise.
/// </summary>
public class MacOsToolTests
{
    [Fact]
    public async Task IconServices_finds_both_cache_folders_and_appends_the_restart_commands()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("com.apple.iconservices.store", "a.bin"), 4096);
        dir.File(Path.Combine("com.apple.IconServices", "b.bin"), 2048);
        dir.File(Path.Combine("com.spotify.client", "unrelated.bin"), 999999);

        var preview = await new IconServicesCacheTool(dir.Path, new FakeRunner()).InspectAsync(default);

        Assert.True(preview.CanRun);
        Assert.Equal(6144, preview.TotalBytes);
        Assert.DoesNotContain(preview.Items, i => i.Label.StartsWith("com.spotify", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Detail == "killall Dock");
        Assert.Contains(preview.Items, i => i.Detail == "killall Finder");
    }

    [Fact]
    public async Task IconServices_deletes_only_the_sized_rows_never_the_command_rows()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("com.apple.iconservices.store", "a.bin"), 1024);
        var runner = new FakeRunner();
        var tool = new IconServicesCacheTool(dir.Path, runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "com.apple.iconservices.store")));
        Assert.Equal(new[] { "killall Dock", "killall Finder" }, runner.RanDisplays);
    }

    [Fact]
    public async Task IconServices_blocks_when_there_is_no_cache()
    {
        using var dir = new TempDir();
        var preview = await new IconServicesCacheTool(dir.Path, new FakeRunner()).InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    [Fact]
    public async Task UserCaches_lists_each_bundle_folder_with_its_size()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("com.apple.Safari", "a.bin"), 1024);
        dir.File(Path.Combine("com.google.Chrome", "b.bin"), 2048);

        var preview = await new UserCachesTool(dir.Path).InspectAsync(default);

        Assert.Equal(2, preview.Items.Count);
        Assert.Equal(3072, preview.TotalBytes);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("YES", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("", false)]
    [InlineData("  1\n", true)]
    public void Finder_reads_every_form_defaults_can_print(string stdout, bool expected)
    {
        Assert.Equal(expected, FinderHiddenFilesTool.ParseShowing(stdout));
    }

    [Fact]
    public void Finder_builds_the_documented_defaults_commands()
    {
        Assert.Equal("defaults read com.apple.finder AppleShowAllFiles",
            FinderHiddenFilesTool.ReadCommand.Display);
        Assert.Equal("defaults write com.apple.finder AppleShowAllFiles -bool true",
            FinderHiddenFilesTool.WriteCommand(true).Display);
        Assert.Equal("defaults write com.apple.finder AppleShowAllFiles -bool false",
            FinderHiddenFilesTool.WriteCommand(false).Display);
    }

    [Fact]
    public async Task Finder_treats_an_unset_key_as_hidden_and_turns_it_on()
    {
        // `defaults read` exits non-zero when the key has never been written. That is
        // the factory state, not an error, and the tool must not read it as "shown".
        var runner = new FakeRunner().WithBinary("defaults")
            .Returns(FinderHiddenFilesTool.ReadCommand.Display, 1,
                stderr: "does not exist");
        var tool = new FinderHiddenFilesTool(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Contains("defaults write com.apple.finder AppleShowAllFiles -bool true", runner.RanDisplays);
        Assert.Contains("killall Finder", runner.RanDisplays);
    }

    [Fact]
    public async Task Finder_turns_it_back_off_when_it_is_already_on()
    {
        var runner = new FakeRunner().WithBinary("defaults")
            .Returns(FinderHiddenFilesTool.ReadCommand.Display, 0, stdout: "1\n");
        var tool = new FinderHiddenFilesTool(runner);

        await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.Contains("defaults write com.apple.finder AppleShowAllFiles -bool false", runner.RanDisplays);
    }

    [Fact]
    public async Task Finder_does_not_restart_finder_when_the_write_failed()
    {
        var runner = new FakeRunner().WithBinary("defaults")
            .Returns(FinderHiddenFilesTool.ReadCommand.Display, 1)
            .Returns(FinderHiddenFilesTool.WriteCommand(true).Display, 1, stderr: "permission denied");
        var tool = new FinderHiddenFilesTool(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.DoesNotContain("killall Finder", runner.RanDisplays);
    }

    [Fact]
    public async Task MacOs_dns_says_out_loud_what_it_cannot_do_without_sudo()
    {
        var runner = new FakeRunner().WithBinary("dscacheutil");
        var tool = new OsXos.Tools.MacOS.FlushDnsTool(runner);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.Contains(preview.Items, i => i.Detail == OsXos.Tools.MacOS.FlushDnsTool.ElevatedCommand);
        Assert.Contains(result.Lines, l => l == OsXos.Tools.MacOS.FlushDnsTool.ElevatedCommand);
        // The elevated half is shown, never run.
        Assert.Equal(new[] { "dscacheutil -flushcache" }, runner.RanDisplays);
    }
}

public class LinuxToolTests
{
    [Fact]
    public async Task Thumbnail_cache_lists_the_freedesktop_subdirectories()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("normal", "a.png"), 1024);
        dir.File(Path.Combine("large", "b.png"), 2048);
        dir.File(Path.Combine("fail", "c.png"), 512);

        var preview = await new ThumbnailCacheTool(dir.Path).InspectAsync(default);

        Assert.Equal(3, preview.Items.Count);
        Assert.Equal(3584, preview.TotalBytes);
    }

    [Fact]
    public async Task User_cache_blocks_when_the_directory_does_not_exist()
    {
        using var dir = new TempDir();
        var preview = await new UserCacheTool(dir.Sub("nope")).InspectAsync(default);
        Assert.False(preview.CanRun);
    }

    [Fact]
    public void Icon_cache_rebuild_builds_one_forced_command_per_theme()
    {
        Assert.Equal("gtk-update-icon-cache -f -t /home/me/.local/share/icons/Papirus",
            IconCacheRebuildTool.CommandFor("/home/me/.local/share/icons/Papirus").Display);
    }

    [Fact]
    public async Task Icon_cache_rebuild_blocks_when_the_binary_is_absent()
    {
        using var dir = new TempDir();
        dir.Dir("Papirus");

        var preview = await new IconCacheRebuildTool(dir.Path, new FakeRunner()).InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.Contains("not installed", preview.Blocker!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Icon_cache_rebuild_blocks_when_there_are_no_user_themes()
    {
        using var dir = new TempDir();
        var runner = new FakeRunner().WithBinary(IconCacheRebuildTool.Binary);

        var preview = await new IconCacheRebuildTool(dir.Path, runner).InspectAsync(default);

        Assert.False(preview.CanRun);
    }

    [Fact]
    public async Task Icon_cache_rebuild_runs_once_per_theme_directory()
    {
        using var dir = new TempDir();
        dir.Dir("Papirus");
        dir.Dir("Adwaita-custom");
        var runner = new FakeRunner().WithBinary(IconCacheRebuildTool.Binary);
        var tool = new IconCacheRebuildTool(dir.Path, runner);

        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.True(result.Ok);
        Assert.Equal(2, preview.Items.Count);
        Assert.Equal(2, runner.Ran.Count);
        Assert.All(runner.Ran, c => Assert.Equal(new[] { "-f", "-t" }, c.Args.Take(2)));
    }

    [Fact]
    public async Task Icon_cache_rebuild_reports_a_partial_success_honestly()
    {
        using var dir = new TempDir();
        var good = dir.Dir("Papirus");
        var bad = dir.Dir("Broken");
        var runner = new FakeRunner().WithBinary(IconCacheRebuildTool.Binary)
            .Returns(IconCacheRebuildTool.CommandFor(bad).Display, 1, stderr: "not a valid icon theme");
        var tool = new IconCacheRebuildTool(dir.Path, runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal("Rebuilt 1 of 2 icon themes", result.Headline);
        Assert.Contains(result.Lines, l => l.Contains("not a valid icon theme", StringComparison.Ordinal));
    }

    [Fact]
    public void Linux_dns_builds_the_resolvectl_command()
    {
        Assert.Equal("resolvectl flush-caches", OsXos.Tools.Linux.FlushDnsTool.Command.Display);
    }

    [Fact]
    public async Task Linux_dns_refuses_to_guess_at_a_resolver_it_cannot_see()
    {
        var preview = await new OsXos.Tools.Linux.FlushDnsTool(new FakeRunner()).InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.Contains("dnsmasq", preview.Blocker!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Linux_dns_runs_resolvectl_when_it_is_present()
    {
        var runner = new FakeRunner().WithBinary("resolvectl");
        var tool = new OsXos.Tools.Linux.FlushDnsTool(runner);

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Equal(new[] { "resolvectl flush-caches" }, runner.RanDisplays);
    }
}

public class ShellCommandTests
{
    [Fact]
    public void Display_quotes_only_arguments_that_need_it()
    {
        Assert.Equal("killall Dock", new ShellCommand("killall", "Dock").Display);
        Assert.Equal("gtk-update-icon-cache -f -t \"/home/a b/icons\"",
            new ShellCommand("gtk-update-icon-cache", "-f", "-t", "/home/a b/icons").Display);
        Assert.Equal("ls", new ShellCommand("ls").Display);
    }

    [Fact]
    public void Message_prefers_stdout_and_falls_back_to_stderr()
    {
        Assert.Equal("done", new ProcessOutcome(0, " done \n", "ignored").Message);
        Assert.Equal("boom", new ProcessOutcome(1, "   ", " boom ").Message);
    }

    [Fact]
    public void Ok_tracks_the_exit_code()
    {
        Assert.True(new ProcessOutcome(0, "", "").Ok);
        Assert.False(new ProcessOutcome(1, "", "").Ok);
        Assert.False(new ProcessOutcome(-1, "", "").Ok);
    }
}

public class XdgPathTests
{
    [Fact]
    public void An_absolute_XDG_CACHE_HOME_is_honoured()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            var absolute = Path.Combine(Path.GetTempPath(), "xdg-cache");
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", absolute);
            Assert.Equal(absolute, XdgPaths.CacheHome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
        }
    }

    [Fact]
    public void A_relative_XDG_CACHE_HOME_is_ignored_as_the_spec_requires()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", "relative/cache");
            Assert.Equal(Path.Combine(XdgPaths.Home, ".cache"), XdgPaths.CacheHome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", original);
        }
    }

    [Fact]
    public void An_unset_XDG_DATA_HOME_falls_back_to_local_share()
    {
        var original = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            Assert.Equal(Path.Combine(XdgPaths.Home, ".local", "share"), XdgPaths.DataHome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", original);
        }
    }
}
