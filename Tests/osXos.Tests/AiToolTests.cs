using OsXos.Tools;
using OsXos.Tools.Ai;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// The AI Assistants category. Every test here runs against a fake profile built under
/// a temp directory — <see cref="AiPaths"/> exists so that the map can be pointed
/// somewhere other than the machine running the tests, and none of these ever touch a
/// real <c>~/.claude</c>.
/// </summary>
public class AiFootprintTests
{
    static AiPaths Fake(TempDir dir) => new(
        dir.Sub("home"), dir.Sub("support"), dir.Sub("cache"), dir.Sub("temp"));

    public static TheoryData<OSKind> AllPlatforms => new() { OSKind.Windows, OSKind.MacOS, OSKind.Linux };

    /// <summary>
    /// The things no tool in this category may ever delete, as absolute paths under a
    /// fake profile. Deliberately a list and not a rule: a credential file quietly
    /// joining the map is the thing worth catching, and taking one off this list is a
    /// small deliberate act.
    /// </summary>
    static IReadOnlyList<string> MustSurvive(AiPaths p) => new[]
    {
        // Logins. Deleting any of these signs the user out of a tool osXos does not own.
        Path.Combine(p.Home, ".claude", ".credentials.json"),
        Path.Combine(p.Home, ".codex", "auth.json"),
        Path.Combine(p.Home, ".codex", ".sandbox-secrets", "token"),
        Path.Combine(p.Home, ".gemini", "oauth_creds.json"),
        Path.Combine(p.Home, ".gemini", "google_accounts.json"),

        // Configuration the user set, and the instruction files they wrote by hand.
        Path.Combine(p.Home, ".claude.json"),
        Path.Combine(p.Home, ".claude", "settings.json"),
        Path.Combine(p.Home, ".codex", "config.toml"),
        Path.Combine(p.Home, ".codex", "AGENTS.md"),
        Path.Combine(p.Home, ".gemini", "settings.json"),
        Path.Combine(p.Home, ".gemini", "trustedFolders.json"),
        Path.Combine(p.Home, ".gemini", "GEMINI.md"),

        // Skills, and the memory stores. Written by hand or earned over months; in
        // neither case something a cleanup tool gets to decide about.
        Path.Combine(p.Home, ".claude", "skills", "mine", "SKILL.md"),
        Path.Combine(p.Home, ".codex", "skills", "mine", "SKILL.md"),
        Path.Combine(p.Home, ".codex", "memories_1.sqlite"),
        Path.Combine(p.Home, ".claude", "projects", "D--Work-repo", "memory", "MEMORY.md"),
        Path.Combine(p.Home, ".claude", "projects", "D--Work-repo", "memory", "a-fact.md"),
    };

    /// <summary>A profile with something in every place the map knows about.</summary>
    static void Populate(TempDir dir, AiPaths p)
    {
        foreach (var path in MustSurvive(p)) Write(path, 32);

        // Scratch
        Write(Path.Combine(p.Home, ".claude", "cache", "a.bin"), 100);
        Write(Path.Combine(p.Home, ".claude", "shell-snapshots", "s.sh"), 200);
        Write(Path.Combine(p.Home, ".claude", "daemon.log"), 300);
        Write(Path.Combine(p.Home, ".codex", ".sandbox-bin", "codex"), 400);
        Write(Path.Combine(p.Home, ".codex", "logs_2.sqlite"), 500);
        Write(Path.Combine(p.Temp, "claude", "session", "scratch.txt"), 600);

        // History
        Write(Path.Combine(p.Home, ".claude", "projects", "D--Work-repo", "chat.jsonl"), 1000);
        Write(Path.Combine(p.Home, ".claude", "projects", "D--Other", "chat.jsonl"), 2000);
        Write(Path.Combine(p.Home, ".claude", "history.jsonl"), 50);
        Write(Path.Combine(p.Home, ".codex", "sessions", "s1.jsonl"), 3000);
        Write(Path.Combine(p.Home, ".gemini", "history", "h.json"), 60);

        // Residue
        Write(Path.Combine(p.Home, ".claude", "plugins", "p", "plugin.json"), 70);
        Write(Path.Combine(p.Home, ".codex", "generated_images", "img.png"), 8000);
        Write(Path.Combine(p.Home, ".gemini", "antigravity-backup", "b.json"), 90);
    }

    static void Write(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    static AiCleanupTool Tool(OSKind os, AiJob job, AiPaths p) => new(os, job, p);

    static async Task<ToolResult> InspectAndRun(AiCleanupTool tool)
    {
        var preview = await tool.InspectAsync(default);
        return await tool.RunAsync(preview, default);
    }

    // ------------------------------------------------------------ the deny-list --

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public async Task Even_the_wipe_tool_leaves_credentials_settings_and_memory_alone(OSKind os)
    {
        // The single most important test in this file: not that the map avoids naming
        // these, but that running the most aggressive of the three tools over a
        // populated profile leaves every one of them on disk.
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        await InspectAndRun(Tool(os, AiJob.Everything, paths));

        Assert.All(MustSurvive(paths), path =>
            Assert.True(File.Exists(path), $"{path} was deleted"));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void No_target_names_a_credential_or_a_setting(OSKind os)
    {
        // The filesystem test above proves the behaviour; this one catches the mistake
        // earlier and names it, by checking no target is one of those paths or the
        // directory immediately holding one.
        using var dir = new TempDir();
        var paths = Fake(dir);

        var targets = AiFootprint.For(os, paths);
        foreach (var forbidden in MustSurvive(paths))
        {
            var holder = Path.GetDirectoryName(forbidden)!;
            Assert.DoesNotContain(targets, t =>
                t.Path.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
                (t.PreservedChild is null && t.Path.Equals(holder, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void Every_target_lives_under_one_of_the_four_roots(OSKind os)
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        string[] roots = { paths.Home, paths.AppSupport, paths.AppCache, paths.Temp };

        Assert.All(AiFootprint.For(os, paths), t =>
            Assert.True(
                roots.Any(r => t.Path.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
                $"{t.Path} escapes the profile"));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void No_target_is_a_duplicate_or_sits_inside_another(OSKind os)
    {
        // Two targets overlapping would count the same bytes twice in the Review
        // total and hand the same path to the sweep twice, the second time as a
        // failure. Both are quiet enough to be worth a test rather than a comment.
        using var dir = new TempDir();
        var targets = AiFootprint.For(os, Fake(dir));
        var paths = targets.Select(t => t.Path).ToList();

        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var outer in paths)
            Assert.DoesNotContain(paths, inner =>
                inner.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------- how the three split --

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public void The_two_narrow_tools_do_not_overlap_and_the_wipe_covers_both(OSKind os)
    {
        using var dir = new TempDir();
        var paths = Fake(dir);

        var caches = Reach(os, paths, AiJob.Caches);
        var history = Reach(os, paths, AiJob.History);
        var everything = Reach(os, paths, AiJob.Everything);

        Assert.Empty(caches.Intersect(history, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(caches.Except(everything, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(history.Except(everything, StringComparer.OrdinalIgnoreCase));

        // The wipe is the whole map, not a larger subset of it.
        Assert.Equal(AiFootprint.For(os, paths).Count, everything.Count);
    }

    /// <summary>Every path one job could touch, taken through the tool's own filter.</summary>
    static List<string> Reach(OSKind os, AiPaths paths, AiJob job) =>
        AiFootprint.For(os, paths, Tool(os, job, paths).Spills).Select(t => t.Path).ToList();

    // ------------------------------------------------------------- memory folder --

    [Fact]
    public async Task A_history_run_empties_a_project_folder_but_keeps_its_memory()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        var project = Path.Combine(paths.Home, ".claude", "projects", "D--Work-repo");
        await InspectAndRun(Tool(OSKind.Windows, AiJob.History, paths));

        Assert.False(File.Exists(Path.Combine(project, "chat.jsonl")));
        Assert.True(File.Exists(Path.Combine(project, "memory", "MEMORY.md")));
        Assert.True(File.Exists(Path.Combine(project, "memory", "a-fact.md")));
    }

    [Fact]
    public void A_project_folder_holding_only_memory_is_not_offered_at_all()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Write(Path.Combine(paths.Home, ".claude", "projects", "D--Quiet", "memory", "MEMORY.md"), 10);

        var target = AiFootprint.For(OSKind.Windows, paths)
            .Single(t => t.PreservedChild is not null);

        Assert.Empty(AiFootprint.Rows(target));
    }

    [Fact]
    public async Task A_project_folders_size_excludes_its_memory()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Write(Path.Combine(paths.Home, ".claude", "projects", "D--Work", "chat.jsonl"), 1000);
        Write(Path.Combine(paths.Home, ".claude", "projects", "D--Work", "memory", "MEMORY.md"), 4096);

        var preview = await Tool(OSKind.Windows, AiJob.History, paths).InspectAsync(default);

        var row = Assert.Single(preview.Items);
        Assert.Equal(1000, row.Bytes);
    }

    // ------------------------------------------------------------------- inspect --

    [Fact]
    public async Task Inspect_changes_nothing()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        var before = Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
        foreach (var job in new[] { AiJob.Caches, AiJob.History, AiJob.Everything })
            await Tool(OSKind.Windows, job, paths).InspectAsync(default);

        Assert.Equal(before, Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories).OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(AllPlatforms))]
    public async Task A_profile_with_no_AI_tools_blocks_with_a_reason(OSKind os)
    {
        using var dir = new TempDir();
        var paths = Fake(dir);

        foreach (var job in new[] { AiJob.Caches, AiJob.History, AiJob.Everything })
        {
            var preview = await Tool(os, job, paths).InspectAsync(default);
            Assert.False(preview.CanRun);
            Assert.False(string.IsNullOrWhiteSpace(preview.Blocker));
            Assert.Empty(preview.Items);
        }
    }

    [Fact]
    public async Task Each_row_names_the_product_it_belongs_to()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        var preview = await Tool(OSKind.Windows, AiJob.Everything, paths).InspectAsync(default);

        Assert.All(preview.Items, i => Assert.Contains(" · ", i.Label));
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Claude Code · ", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Codex · ", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Label.StartsWith("Gemini & Antigravity · ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tool_that_is_not_installed_contributes_nothing_rather_than_failing()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Write(Path.Combine(paths.Home, ".claude", "cache", "a.bin"), 100);

        var preview = await Tool(OSKind.Windows, AiJob.Caches, paths).InspectAsync(default);

        var row = Assert.Single(preview.Items);
        Assert.StartsWith("Claude Code · ", row.Label, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------------- run --

    [Fact]
    public async Task A_caches_run_deletes_the_scratch_and_leaves_the_transcripts()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        var result = await InspectAndRun(Tool(OSKind.Windows, AiJob.Caches, paths));

        Assert.True(result.Ok);
        Assert.False(Directory.Exists(Path.Combine(paths.Home, ".claude", "cache")));
        Assert.False(File.Exists(Path.Combine(paths.Home, ".claude", "daemon.log")));
        Assert.False(Directory.Exists(Path.Combine(paths.Temp, "claude")));

        Assert.True(File.Exists(Path.Combine(paths.Home, ".codex", "sessions", "s1.jsonl")));
        Assert.True(File.Exists(Path.Combine(paths.Home, ".claude", "projects", "D--Other", "chat.jsonl")));
    }

    [Fact]
    public async Task A_history_run_leaves_the_caches_alone()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        await InspectAndRun(Tool(OSKind.Windows, AiJob.History, paths));

        Assert.True(Directory.Exists(Path.Combine(paths.Home, ".claude", "cache")));
        Assert.True(File.Exists(Path.Combine(paths.Home, ".codex", "logs_2.sqlite")));
        Assert.False(Directory.Exists(Path.Combine(paths.Home, ".codex", "sessions")));
    }

    [Fact]
    public async Task The_wipe_takes_the_residue_the_other_two_leave()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        await InspectAndRun(Tool(OSKind.Windows, AiJob.Everything, paths));

        Assert.False(Directory.Exists(Path.Combine(paths.Home, ".codex", "generated_images")));
        Assert.False(Directory.Exists(Path.Combine(paths.Home, ".claude", "plugins")));
        Assert.False(Directory.Exists(Path.Combine(paths.Home, ".gemini", "antigravity-backup")));
    }

    [Fact]
    public async Task The_result_reports_what_it_actually_reclaimed()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Write(Path.Combine(paths.Home, ".claude", "cache", "a.bin"), 4096);

        var tool = Tool(OSKind.Windows, AiJob.Caches, paths);
        var preview = await tool.InspectAsync(default);
        var result = await tool.RunAsync(preview, default);

        Assert.Equal(4096, preview.TotalBytes);
        Assert.True(result.Ok);
        Assert.Contains("4 KB", result.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Progress_is_reported_so_a_long_run_does_not_look_frozen()
    {
        using var dir = new TempDir();
        var paths = Fake(dir);
        Populate(dir, paths);

        // Recorded synchronously rather than through Progress<T>, which posts to a
        // synchronisation context the test does not have and would race the assert.
        var seen = new RecordingProgress();
        var tool = Tool(OSKind.Windows, AiJob.Everything, paths);
        await tool.RunAsync(await tool.InspectAsync(default), default, seen);

        Assert.NotEmpty(seen.Reports);
        Assert.All(seen.Reports, r => Assert.True(r.IsCountable));
        Assert.Equal(seen.Reports[^1].Total, seen.Reports[^1].Done);
    }

    sealed class RecordingProgress : IProgress<ToolProgress>
    {
        public List<ToolProgress> Reports { get; } = new();
        public void Report(ToolProgress value) => Reports.Add(value);
    }
}

/// <summary>
/// How the three tools present themselves, checked through <see cref="ITool"/> the way
/// the app sees them rather than through the concrete class.
/// </summary>
public class AiCleanupToolTests
{
    static IEnumerable<AiCleanupTool> All(OSKind os) =>
        new[] { AiJob.Caches, AiJob.History, AiJob.Everything }
            .Select(job => new AiCleanupTool(os, job));

    [Fact]
    public void All_three_appear_in_the_AI_category_on_every_platform()
    {
        foreach (var os in new[] { OSKind.Windows, OSKind.MacOS, OSKind.Linux })
        {
            var ai = All(os).ToList();
            Assert.Equal(3, ai.Count);
            Assert.All(ai, t => Assert.Equal(ToolCategory.AI, t.Category));
            Assert.All(ai, t => Assert.NotNull(CategoryCatalog.Find(os, ToolCategory.AI)));
        }
    }

    [Fact]
    public void None_of_them_asks_for_administrator_rights()
    {
        // Everything they touch is inside the user's own profile. If that ever stops
        // being true, ElevationTests.MayElevate is the other half of this.
        // RequiresElevation is a default interface member, so it is only visible
        // through ITool — which is how the app sees it too.
        foreach (var os in new[] { OSKind.Windows, OSKind.MacOS, OSKind.Linux })
            Assert.All(All(os).Cast<ITool>(), t => Assert.False(t.RequiresElevation));
    }

    [Fact]
    public void All_three_are_destructive_and_say_what_they_cost()
    {
        Assert.All(All(OSKind.Windows), t =>
        {
            Assert.True(t.IsDestructive);
            Assert.False(string.IsNullOrWhiteSpace(t.Warning));
            Assert.True(t.Steps.Count >= 4);
        });
    }

    [Fact]
    public void The_two_unrecoverable_ones_say_so_in_the_warning()
    {
        foreach (var job in new[] { AiJob.History, AiJob.Everything })
        {
            var tool = new AiCleanupTool(OSKind.Windows, job);
            Assert.Contains("do not come back", tool.Warning!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ids_are_stable_and_distinct_per_platform_and_job()
    {
        var ids = new[] { OSKind.Windows, OSKind.MacOS, OSKind.Linux }
            .SelectMany(All).Select(t => t.Id).ToList();

        Assert.Equal(9, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("windows.ai-caches", ids);
        Assert.Contains("macos.ai-history", ids);
        Assert.Contains("linux.ai-leftovers", ids);
    }
}
