namespace OsXos.Tools.Ai;

/// <summary>
/// Which of the four jobs in the AI Assistants category a tool instance is. The only
/// thing that differs between them is the <see cref="AiSpill"/> filter and the prose
/// explaining it, so they are one class four times rather than four classes.
/// </summary>
public enum AiJob
{
    /// <summary>Temp only. Nothing here is even a cache.</summary>
    Temp,

    /// <summary>Temp and scratch. Nothing here is unrecoverable.</summary>
    Caches,

    /// <summary>Transcripts only. Everything here is unrecoverable.</summary>
    History,

    /// <summary>Both of the above plus the residue nothing else claims.</summary>
    Everything,
}

/// <summary>
/// Finds what Claude, Codex and Antigravity have left under your profile, and — once
/// you have seen the list — deletes it.
///
/// The map lives in <see cref="AiFootprint"/> and is a fixed list of known locations,
/// never a search. Credentials, configuration, hand-written instruction files, skills
/// and memory stores are not in that map at all, so no instance of this tool can
/// reach them, including the one called Remove AI Tool Leftovers.
/// </summary>
public sealed class AiCleanupTool : ITool
{
    readonly ProfileRoots _paths;

    public AiCleanupTool(OSKind platform, AiJob job)
        : this(platform, job, ProfileRoots.Current(platform)) { }

    public AiCleanupTool(OSKind platform, AiJob job, ProfileRoots paths)
    {
        Platform = platform;
        Job = job;
        _paths = paths;
    }

    public AiJob Job { get; }
    public OSKind Platform { get; }
    public ToolCategory Category => ToolCategory.AI;

    /// <summary>What this job is allowed to touch. The whole difference between the three.</summary>
    public IReadOnlyCollection<AiSpill> Spills => Job switch
    {
        AiJob.Temp => new[] { AiSpill.Temp },
        AiJob.Caches => new[] { AiSpill.Temp, AiSpill.Scratch },
        AiJob.History => new[] { AiSpill.History },
        _ => new[] { AiSpill.Temp, AiSpill.Scratch, AiSpill.History, AiSpill.Residue },
    };

    public string Id => Platform switch
    {
        OSKind.Windows => "windows." + Slug,
        OSKind.MacOS => "macos." + Slug,
        _ => "linux." + Slug,
    };

    string Slug => Job switch
    {
        AiJob.Temp => "ai-temp",
        AiJob.Caches => "ai-caches",
        AiJob.History => "ai-history",
        _ => "ai-leftovers",
    };

    public string Name => Job switch
    {
        AiJob.Temp => "Clear AI Temp Files",
        AiJob.Caches => "Clear AI Tool Caches",
        AiJob.History => "Clear AI Assistant History",
        _ => "Remove AI Tool Leftovers",
    };

    public string Summary => Job switch
    {
        AiJob.Temp => "Delete the per-session scratchpads, temp folders and pasted images Claude and Codex never come back for.",
        AiJob.Caches => "Delete the scratch, logs and installer payloads Claude, Codex and Antigravity leave behind.",
        AiJob.History => "Delete the stored transcripts of your conversations with Claude, Codex and Antigravity.",
        _ => "Remove everything these tools have left under your profile, short of your logins and settings.",
    };

    public string IconKey => Job switch
    {
        AiJob.Temp => "IconFolder",
        AiJob.Caches => "IconTrash",
        AiJob.History => "IconPrivacy",
        _ => "IconAI",
    };

    public bool IsDestructive => true;

    /// <summary>
    /// Each wider job includes the narrower ones whole: Caches takes everything Temp
    /// does, and the wipe takes everything.
    /// </summary>
    public IReadOnlyCollection<string> Covers => Job switch
    {
        AiJob.Caches => new[] { IdFor(AiJob.Temp) },
        AiJob.Everything => new[] { IdFor(AiJob.Temp), IdFor(AiJob.Caches), IdFor(AiJob.History) },
        _ => Array.Empty<string>(),
    };

    string IdFor(AiJob job) => new AiCleanupTool(Platform, job, _paths).Id;

    public string? Warning => Job switch
    {
        AiJob.Temp =>
            "Close Claude Code and Codex first — a session that is still running is using its scratchpad, and anything it holds open is skipped rather than forced. " +
            "Nothing here is read again once its session ends: not a cache, not a transcript, and not missed.",

        AiJob.Caches =>
            "Close Claude, Codex and Antigravity first — a running one holds its own scratch open and will rewrite it as soon as this finishes. " +
            "Nothing here is irreplaceable: the next launch of each tool is slower while it rebuilds, and that is the whole cost.",

        AiJob.History =>
            "Your conversations do not come back. There is no undo and nothing goes to the Recycle Bin. " +
            "Memory files, credentials and settings are not touched — this is the record of what was said, and only that.",

        _ =>
            "This is both of the other two tools plus everything else these tools have left lying about: extensions, plugins, generated images, state databases. " +
            "Your conversations do not come back. You stay signed in and your settings, skills and memory survive, but the tools will behave like a fresh install in every other respect.",
    };

    public IReadOnlyList<ToolStep> Steps => Job switch
    {
        AiJob.Temp => new ToolStep[]
        {
            new("Look where these tools put throwaway files",
                @"The per-session scratchpads every Claude Code run creates under the system temp folder, Claude Code's shell snapshots and session environments, Codex's two temp folders under ~/.codex, and the files Codex drops loose in the system temp folder — every image pasted into a prompt, and the git index each snapshot of your working tree is built with."),
            new("Match by the tool's own name, never by shape",
                "The loose files are found by the prefix Codex gives them — codex-clipboard-, codex-index- — and nothing else in the temp folder is looked at. Emptying the whole temp folder is a different job, and not this one."),
            new("Measure each one before anything is touched",
                "The Review stage lists every location with its real size on this machine. The scan only reads, and a tool that is not installed here simply contributes nothing."),
            new("Delete the rest",
                "Files held open by a session that is still running are skipped rather than forced, counted, and named afterwards. Caches, transcripts, logins, settings, skills and memory are not in this tool's reach at all — the next launch is not even slower."),
        },

        AiJob.Caches => new ToolStep[]
        {
            new("Look in the places these tools cache things",
                @"Everything Clear AI Temp Files takes, plus Claude Code's cache, downloads and file-edit undo history; Codex's sandbox binaries and log database; the Electron cache folders under Claude Desktop and Antigravity; and the installer packages Claude Desktop keeps after updating. All of it inside your own profile, so no administrator rights are needed."),
            new("Measure each one before anything is touched",
                "The Review stage lists every location with its real size on this machine. The scan only reads. If a tool is not installed here, it simply contributes nothing rather than being reported as a problem."),
            new("Nothing that matters is on the list",
                "Credentials, settings, hand-written instruction files, skills and memory are not part of the map this tool searches, so they cannot appear on the Review list and cannot be deleted by pressing the button on it."),
            new("Delete the rest",
                "Files held open by a running tool are skipped rather than forced, counted, and named afterwards. Everything here is reproducible by definition — that is what makes it a cache — so the only cost is a slower next launch."),
        },

        AiJob.History => new ToolStep[]
        {
            new("Find the stored conversations",
                @"Claude Code keeps a transcript per project under ~/.claude/projects and a prompt history beside it; Codex keeps its sessions and a thread-history database under ~/.codex; Antigravity keeps a prompt history under ~/.gemini. These are the full text of what you and the assistant said to each other."),
            new("Keep what is not a transcript",
                "Every project folder is listed with the size of its transcripts alone. The memory/ folder inside each one is excluded from that figure and from the deletion — it is hand-written, not a record of a conversation, and it survives this tool."),
            new("Read the list first",
                "Review names every project and every database with its size. Nothing is deleted while you read it, and closing the window at that point leaves the machine exactly as it was."),
            new("Delete the transcripts",
                "Permanently. They do not go to the Recycle Bin and there is no undo — an assistant that could reconstruct the conversation would not have needed to store it. Your logins and settings are untouched, so the tools keep working; they simply will not remember what you talked about."),
        },

        _ => new ToolStep[]
        {
            new("Everything the other tools find, in one list",
                "The temp files, caches and scratch that Clear AI Tool Caches would remove, and the transcripts that Clear AI Assistant History would remove, together."),
            new("Plus what neither of them claims",
                "Installed plugins and IDE extensions, generated images and visualizations, state and queue databases, editor backups, workspace storage, the browser profile Antigravity keeps. None of it reproducible, none of it a transcript, all of it left behind."),
            new("What survives, deliberately",
                "Your logins — .credentials.json, auth.json, oauth_creds.json — and your settings, your hand-written CLAUDE.md and GEMINI.md, your skills, and your memory stores. None of those are in the map this tool searches. It is a thorough clean, not a sign-out."),
            new("Read the list, then delete it",
                "Review names every location with its size before anything is touched. After the run these tools behave like a fresh install that happens to already know who you are, and each will rebuild what it needs on its next launch."),
        },
    };

    IReadOnlyList<AiTarget> Targets() => AiFootprint.For(Platform, _paths, Spills);

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var targets = Targets();
        var items = new List<PreviewItem>();

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            items.AddRange(AiFootprint.Rows(target, ct));
        }

        if (items.Count == 0)
            return Task.FromResult(ToolPreview.Blocked(NothingFound));

        var products = items
            .Select(i => i.Label.Split(" · ")[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var summary =
            $"{items.Count:N0} location{(items.Count == 1 ? "" : "s")} · " +
            $"{FileSweep.FormatBytes(items.Sum(i => i.Bytes ?? 0))} to reclaim · " +
            string.Join(", ", products);

        return Task.FromResult(new ToolPreview(items, summary));
    }

    string NothingFound => Job switch
    {
        AiJob.Temp => "Nothing to clear — none of the AI tools osXos knows about has left temp files on this machine.",
        AiJob.Caches => "Nothing to clear — none of the AI tools osXos knows about has left a cache on this machine.",
        AiJob.History => "Nothing to clear — no stored conversations were found for any of the AI tools osXos knows about.",
        _ => "Nothing to remove — none of the AI tools osXos knows about has left anything on this machine.",
    };

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var sweep = AiFootprint.Delete(Targets(), preview.Items, progress, ct);

        var lines = new List<string>
        {
            $"Removed {sweep.Deleted:N0} item{(sweep.Deleted == 1 ? "" : "s")} across {preview.Items.Count:N0} location{(preview.Items.Count == 1 ? "" : "s")}, reclaiming {FileSweep.FormatBytes(sweep.Freed)}.",
        };

        if (sweep.Skipped > 0)
        {
            lines.Add($"{sweep.Skipped:N0} left in place — held open by a running tool.");
            lines.AddRange(sweep.Problems);
        }

        if (sweep.Deleted == 0)
            return Task.FromResult(ToolResult.Failure("Nothing could be removed", lines.ToArray()));

        lines.Add(Job switch
        {
            AiJob.Temp => "Caches, transcripts, credentials and settings were not touched.",
            AiJob.Caches => "Each tool rebuilds its cache on the next launch.",
            AiJob.History => "Memory, credentials and settings were not touched.",
            _ => "You are still signed in, and your settings, skills and memory were not touched.",
        });

        return Task.FromResult(ToolResult.Success(
            $"{FileSweep.FormatBytes(sweep.Freed)} reclaimed from {preview.Items.Count:N0} location{(preview.Items.Count == 1 ? "" : "s")}",
            lines.ToArray()));
    }
}
