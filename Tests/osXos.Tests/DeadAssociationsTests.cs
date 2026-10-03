using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// Remove Dead App Associations, against an in-memory registry and a scripted file
/// system. What matters most here is what it leaves alone: working programs, apps on
/// a drive that is not plugged in, installed Store apps, and anything machine-wide.
/// </summary>
public class DeadAssociationsTests
{
    const string FileExts = DeadAssociationsTool.FileExts;
    const string Classes = DeadAssociationsTool.Classes;

    sealed class Rig
    {
        public FakeRegistry Registry { get; } = new();
        public FakeUserRegistry User { get; } = new();
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MissingDrives { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OnPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Notified { get; private set; }

        public DeadAssociationsTool Tool() => new(Registry, User, () => Notified++,
            Files.Contains,
            path => !MissingDrives.Contains(Path.GetPathRoot(path) ?? ""),
            name => OnPath.Contains(name) ? @"C:\Windows\System32\" + name : null);

        /// <summary>Registers an application the way an installer does, under one hive.</summary>
        public Rig App(RegHive hive, string exeName, string path)
        {
            Registry.Set(hive, $@"{Classes}\Applications\{exeName}\shell\open\command", "", $"\"{path}\" \"%1\"");
            return this;
        }

        public Rig ProgId(RegHive hive, string progId, string path)
        {
            Registry.Set(hive, $@"{Classes}\{progId}\shell\open\command", "", $"\"{path}\" \"%1\"");
            return this;
        }
    }

    static async Task<ToolResult> InspectAndRun(ITool tool) => await tool.RunAsync(await tool.InspectAsync(default), default);

    // ------------------------------------------------------------ detection --

    [Fact]
    public async Task A_program_whose_file_is_gone_is_found_everywhere_it_is_named()
    {
        var rig = new Rig();
        rig.App(RegHive.CurrentUser, "timp.exe", @"D:\Apps\Timp\timp.exe");
        rig.Registry
            .Set(RegHive.CurrentUser, $@"{FileExts}\.mp3\OpenWithList", "a", "timp.exe")
            .Set(RegHive.CurrentUser, $@"{FileExts}\.mp3\OpenWithList", "MRUList", "a")
            .Set(RegHive.CurrentUser, $@"{FileExts}\.mp3\UserChoice", "ProgId", @"Applications\timp.exe");

        var preview = await rig.Tool().InspectAsync(default);

        var rows = preview.Items.Select(i => i.Label).ToList();
        Assert.Contains(@"timp.exe — D:\Apps\Timp\timp.exe is gone", rows);
        Assert.Contains(@"Applications\timp.exe — D:\Apps\Timp\timp.exe is gone", rows);
        var timp = preview.Items.Single(i => i.Label.StartsWith("timp.exe", StringComparison.Ordinal));
        Assert.Contains(".mp3 · Open with list", timp.Detail, StringComparison.Ordinal);
        Assert.Contains("your program registration", timp.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Working_programs_and_your_choices_for_them_are_not_listed()
    {
        var rig = new Rig();
        rig.Files.Add(@"C:\Program Files\VLC\vlc.exe");
        rig.App(RegHive.LocalMachine, "vlc.exe", @"C:\Program Files\VLC\vlc.exe")
           .ProgId(RegHive.LocalMachine, "VLC.mp3", @"C:\Program Files\VLC\vlc.exe");
        rig.Registry
            .Set(RegHive.CurrentUser, $@"{FileExts}\.mp3\OpenWithList", "a", "vlc.exe")
            .Set(RegHive.CurrentUser, $@"{FileExts}\.mp3\OpenWithProgids", "VLC.mp3", new byte[0])
            .Set(RegHive.CurrentUser, $@"{FileExts}\.mp3\UserChoice", "ProgId", "VLC.mp3");

        var preview = await rig.Tool().InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.Contains("every program", preview.Blocker, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_program_on_a_drive_that_is_not_plugged_in_is_never_called_dead()
    {
        var rig = new Rig();
        rig.MissingDrives.Add(@"E:\");
        rig.App(RegHive.CurrentUser, "portable.exe", @"E:\PortableApps\portable.exe");

        Assert.False((await rig.Tool().InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Installed_store_apps_are_kept_and_uninstalled_ones_are_found()
    {
        var rig = new Rig();
        var list = $@"{FileExts}\.md\OpenWithList";
        rig.Registry
            .AddKey(RegHive.CurrentUser, $@"{DeadAssociationsTool.Packages}\19282JackieLiu.Notepads-Beta_1.5.6.0_x64__echhpq9pdbte8")
            .Set(RegHive.CurrentUser, list, "a", "19282JackieLiu.Notepads-Beta_echhpq9pdbte8!App")
            .Set(RegHive.CurrentUser, list, "b", "45907smallapp.HEICViewer_z9hw59krvrfng!App");

        var preview = await rig.Tool().InspectAsync(default);

        var row = Assert.Single(preview.Items);
        Assert.StartsWith("45907smallapp.HEICViewer", row.Label, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("19282JackieLiu.Notepads-Beta_1.5.6.0_x64__echhpq9pdbte8", "19282JackieLiu.Notepads-Beta_echhpq9pdbte8")]
    [InlineData("Microsoft.WindowsNotepad_11.2607.14.0_x64__8wekyb3d8bbwe", "Microsoft.WindowsNotepad_8wekyb3d8bbwe")]
    [InlineData("nounderscores", null)]
    public void A_package_full_name_reduces_to_its_family(string full, string? family)
    {
        Assert.Equal(family, DeadAssociationsTool.FamilyOf(full));
    }

    [Fact]
    public async Task A_bare_name_windows_cannot_resolve_is_left_alone()
    {
        // Open with never shows an entry it cannot resolve, so it is not a visible leftover.
        var rig = new Rig();
        rig.Registry.Set(RegHive.CurrentUser, $@"{FileExts}\.jpg\OpenWithList", "a", "Discord.exe");

        Assert.False((await rig.Tool().InspectAsync(default)).CanRun);
    }

    [Fact]
    public async Task Strings_that_are_not_program_names_are_grouped_as_junk()
    {
        var rig = new Rig();
        rig.Registry
            .Set(RegHive.CurrentUser, $@"{FileExts}\.json\OpenWithList", "b", "ab")
            .Set(RegHive.CurrentUser, $@"{FileExts}\.json\OpenWithList", "c", "fagedbc")
            .Set(RegHive.CurrentUser, $@"{FileExts}\.json\OpenWithList", "d", "Microsoft.AutoGenerated.{3B6E7DF3}");

        var preview = await rig.Tool().InspectAsync(default);

        var row = Assert.Single(preview.Items);
        Assert.StartsWith(DeadAssociationsTool.JunkLabel, row.Label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_type_registered_nowhere_is_a_dangling_reference()
    {
        var rig = new Rig();
        rig.Registry.Set(RegHive.CurrentUser, $@"{FileExts}\.docx\OpenWithProgids", "Word.Document.12", new byte[0]);

        var row = Assert.Single((await rig.Tool().InspectAsync(default)).Items);
        Assert.Equal("Word.Document.12 — no longer registered anywhere", row.Label);
    }

    [Fact]
    public async Task A_machine_wide_dead_registration_is_counted_but_never_offered()
    {
        var rig = new Rig();
        rig.App(RegHive.LocalMachine, "old.exe", @"C:\Program Files\Old\old.exe");

        var preview = await rig.Tool().InspectAsync(default);

        Assert.False(preview.CanRun);
        Assert.Contains("1 dead machine-wide registration", preview.Blocker, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_lines_resolve_to_their_program_or_to_nothing()
    {
        Assert.Equal(@"C:\Program Files\X\x.exe", DeadAssociationsTool.ExecutableOf("\"C:\\Program Files\\X\\x.exe\" \"%1\""));
        Assert.Equal(@"C:\Program Files\X\x.exe", DeadAssociationsTool.ExecutableOf(@"C:\Program Files\X\x.exe %1"));
        Assert.Null(DeadAssociationsTool.ExecutableOf("rundll32.exe shell32.dll,OpenAs_RunDLL %1"));
        Assert.Null(DeadAssociationsTool.ExecutableOf(null));
    }

    // ------------------------------------------------------------------ run --

    [Fact]
    public async Task Run_removes_exactly_the_dead_references_and_keeps_the_list_order()
    {
        var rig = new Rig();
        rig.Files.Add(@"C:\Program Files\VLC\vlc.exe");
        rig.App(RegHive.LocalMachine, "vlc.exe", @"C:\Program Files\VLC\vlc.exe")
           .App(RegHive.CurrentUser, "timp.exe", @"D:\Apps\Timp\timp.exe");
        var list = $@"{FileExts}\.mp3\OpenWithList";
        rig.Registry
            .Set(RegHive.CurrentUser, list, "a", "vlc.exe")
            .Set(RegHive.CurrentUser, list, "b", "timp.exe")
            .Set(RegHive.CurrentUser, list, "MRUList", "ba")
            .Set(RegHive.CurrentUser, $@"{FileExts}\.mp3\UserChoice", "ProgId", @"Applications\timp.exe");

        var result = await InspectAndRun(rig.Tool());

        Assert.True(result.Ok);
        Assert.Equal("vlc.exe", rig.Registry.GetValue(RegHive.CurrentUser, list, "a"));
        Assert.Null(rig.Registry.GetValue(RegHive.CurrentUser, list, "b"));
        Assert.Equal("a", rig.User.GetString(list, "MRUList"));
        Assert.False(rig.Registry.KeyExists(RegHive.CurrentUser, $@"{FileExts}\.mp3\UserChoice"));
        Assert.False(rig.Registry.KeyExists(RegHive.CurrentUser, $@"{Classes}\Applications\timp.exe"));
        Assert.True(rig.Registry.KeyExists(RegHive.LocalMachine, $@"{Classes}\Applications\vlc.exe"));
        Assert.Equal(1, rig.Notified);
    }

    [Fact]
    public async Task Inspect_changes_nothing()
    {
        var rig = new Rig();
        rig.App(RegHive.CurrentUser, "gone.exe", @"C:\Gone\gone.exe");

        await rig.Tool().InspectAsync(default);

        Assert.True(rig.Registry.KeyExists(RegHive.CurrentUser, $@"{Classes}\Applications\gone.exe"));
        Assert.Empty(rig.Registry.Deleted);
        Assert.Equal(0, rig.Notified);
    }

    [Fact]
    public async Task The_state_counts_dead_programs()
    {
        var rig = new Rig();
        Assert.Equal("Clean", (await rig.Tool().ReadStateAsync(default)).Label);

        rig.App(RegHive.CurrentUser, "gone.exe", @"C:\Gone\gone.exe");
        Assert.Equal("1 dead", (await rig.Tool().ReadStateAsync(default)).Label);
    }

    [Fact]
    public void It_sits_in_explorer_and_shell_without_elevation()
    {
        var tool = TestCatalog.Windows().Single(t => t.Id == "windows.dead-associations");
        Assert.Equal(ToolCategory.Shell, tool.Category);
        Assert.False(tool.RequiresElevation);
        Assert.True(tool.IsDestructive);
    }
}
