using OsXos.Tools;
using Xunit;

namespace OsXos.Tests;

public class FormatBytesTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "2 KB")]          // KB is whole-number; 1.5 rounds to even
    [InlineData(1048576, "1 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1649267441664, "1.5 TB")]
    public void Formats_at_the_right_unit(long bytes, string expected)
    {
        Assert.Equal(expected, FileSweep.FormatBytes(bytes));
    }

    [Fact]
    public void Negative_is_not_pretended_to_be_a_size()
    {
        Assert.Equal("—", FileSweep.FormatBytes(-1));
    }

    [Fact]
    public void Never_runs_past_the_largest_unit()
    {
        Assert.EndsWith("TB", FileSweep.FormatBytes(long.MaxValue));
    }
}

public class FileSweepTests
{
    [Fact]
    public void Files_matches_only_the_given_patterns()
    {
        using var dir = new TempDir();
        dir.File("iconcache_16.db", 100);
        dir.File("iconcache_256.db", 200);
        dir.File("thumbcache_32.db", 300);
        // Must not be swept up: same folder, nothing to do with the icon cache.
        dir.File("ExplorerStartupLog.etl", 999);
        dir.File("notes.txt", 999);
        dir.File("iconcache.db.bak", 999);

        var found = FileSweep.Files(dir.Path, "iconcache_*.db", "thumbcache_*.db").ToList();

        Assert.Equal(3, found.Count);
        Assert.Equal(600, found.Sum(f => f.Bytes ?? 0));
        Assert.DoesNotContain(found, f => f.Label.EndsWith(".etl", StringComparison.Ordinal));
        Assert.DoesNotContain(found, f => f.Label.EndsWith(".bak", StringComparison.Ordinal));
    }

    [Fact]
    public void Files_carries_the_full_path_and_size_on_each_item()
    {
        using var dir = new TempDir();
        var path = dir.File("iconcache_16.db", 42);

        var item = Assert.Single(FileSweep.Files(dir.Path, "iconcache_*.db"));

        Assert.Equal("iconcache_16.db", item.Label);
        Assert.Equal(path, item.Detail);
        Assert.Equal(42, item.Bytes);
    }

    [Fact]
    public void Files_never_returns_the_same_path_twice_when_patterns_overlap()
    {
        using var dir = new TempDir();
        dir.File("iconcache_16.db", 10);

        Assert.Single(FileSweep.Files(dir.Path, "iconcache_*.db", "icon*.db", "*.db"));
    }

    [Fact]
    public void Files_is_not_recursive()
    {
        using var dir = new TempDir();
        dir.File("iconcache_16.db", 10);
        dir.File(Path.Combine("nested", "iconcache_32.db"), 10);

        Assert.Single(FileSweep.Files(dir.Path, "iconcache_*.db"));
    }

    [Fact]
    public void A_missing_directory_yields_nothing_rather_than_throwing()
    {
        using var dir = new TempDir();
        Assert.Empty(FileSweep.Files(dir.Sub("never-existed"), "*.db"));
        Assert.Empty(FileSweep.Children(dir.Sub("never-existed")));
        Assert.Equal(0, FileSweep.SizeOf(dir.Sub("never-existed")));
    }

    [Fact]
    public void Children_lists_subdirectories_with_their_recursive_size()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("Chrome", "a.bin"), 500);
        dir.File(Path.Combine("Chrome", "deep", "b.bin"), 500);
        dir.File(Path.Combine("Spotify", "c.bin"), 250);
        dir.File("loose.tmp", 125);

        var items = FileSweep.Children(dir.Path).ToList();

        Assert.Equal(3, items.Count);
        Assert.Equal(1000, items.Single(i => i.Label == "Chrome/").Bytes);
        Assert.Equal(250, items.Single(i => i.Label == "Spotify/").Bytes);
        Assert.Equal(125, items.Single(i => i.Label == "loose.tmp").Bytes);
    }

    [Fact]
    public void Delete_removes_files_and_directories_and_reports_what_it_freed()
    {
        using var dir = new TempDir();
        dir.File(Path.Combine("Chrome", "a.bin"), 500);
        dir.File("loose.tmp", 125);

        var items = FileSweep.Children(dir.Path).ToList();
        var outcome = FileSweep.Delete(items);

        Assert.Equal(2, outcome.Deleted);
        Assert.Equal(0, outcome.Skipped);
        Assert.Equal(625, outcome.Freed);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void Delete_counts_a_locked_file_as_skipped_rather_than_failing()
    {
        using var dir = new TempDir();
        var path = dir.File("held-open.db", 64);
        dir.File("free.db", 64);

        var items = FileSweep.Files(dir.Path, "*.db").ToList();

        // An open handle with no sharing is exactly what explorer.exe does to the
        // icon cache, which is the case this whole code path exists for.
        using (var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = FileSweep.Delete(items);

            Assert.Equal(1, outcome.Deleted);
            Assert.Equal(1, outcome.Skipped);
            Assert.Equal(64, outcome.Freed);
            Assert.Single(outcome.Problems);
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Delete_ignores_an_item_that_has_already_gone()
    {
        using var dir = new TempDir();
        var items = new[] { new PreviewItem("gone.db", dir.Sub("gone.db"), 100) };

        var outcome = FileSweep.Delete(items);

        Assert.Equal(0, outcome.Deleted);
        Assert.Equal(0, outcome.Skipped);
        Assert.Equal(0, outcome.Freed);
    }

    [Fact]
    public void Delete_ignores_an_item_with_no_path_such_as_a_command_row()
    {
        // The macOS icon tool mixes command rows into its preview; those must not be
        // mistaken for something to remove.
        var outcome = FileSweep.Delete(new[] { new PreviewItem("Then run", "killall Dock") });

        Assert.Equal(0, outcome.Deleted);
        Assert.Equal(0, outcome.Skipped);
    }
}
