namespace OsXos.Tools.Ai;

/// <summary>
/// What kind of thing an AI assistant left behind. The three tools in the AI
/// Assistants category are each a filter over this — which is the whole reason the
/// distinction is a value and not a comment.
/// </summary>
public enum AiSpill
{
    /// <summary>
    /// Reproducible by definition: caches, logs, sandbox binaries, scratch folders,
    /// downloaded installer payloads. Deleting any of it costs a slower next launch
    /// and nothing else.
    /// </summary>
    Scratch,

    /// <summary>
    /// The record of what you and the assistant said to each other. Deleting it is
    /// not recoverable and is not something a tool should do as a side effect, which
    /// is why it is a separate tool rather than a checkbox.
    /// </summary>
    History,

    /// <summary>
    /// Everything else that is neither reproducible nor a transcript: extension and
    /// plugin payloads, generated images, state and queue databases, editor backups.
    /// Only the wipe tool claims this.
    /// </summary>
    Residue,
}

/// <summary>
/// One place an AI assistant leaves things. <paramref name="Path"/> is absolute and
/// always resolved from an <see cref="AiPaths"/>, never from the environment directly,
/// so the whole map can be pointed at a temp directory in a test.
/// </summary>
public sealed record AiTarget(string Product, string Label, string Path, AiSpill Spill)
{
    /// <summary>
    /// Set when this target is a container rather than a thing: its immediate
    /// subdirectories become the rows, and inside each of those a child of this name
    /// is left alone.
    ///
    /// <c>~/.claude/projects</c> is the shape this exists for. Each project folder
    /// holds the transcripts that are the entire point of the history tool sitting
    /// next to a <c>memory/</c> folder that is hand-written and must survive.
    /// </summary>
    public string? PreservedChild { get; init; }
}

/// <summary>
/// The four roots every AI tool path is built from, as one value. Supplying them
/// rather than reading the environment is what lets <see cref="AiFootprint"/> be a
/// pure function — the tests build a fake home under a temp directory, and
/// <see cref="AiCleanupTool.RunAsync"/> rebuilds the same map it inspected instead of
/// carrying a mutable list between the two stages.
/// </summary>
public sealed record AiPaths(string Home, string AppSupport, string AppCache, string Temp)
{
    /// <summary>
    /// Where this OS actually keeps the four. <c>AppSupport</c> is the roaming,
    /// configuration-shaped root and <c>AppCache</c> the local, throwaway-shaped one;
    /// Windows splits them as Roaming and Local, macOS as Application Support and
    /// Caches, Linux as the XDG config and cache homes.
    /// </summary>
    public static AiPaths Current(OSKind os)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return os switch
        {
            OSKind.Windows => new AiPaths(
                home,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                System.IO.Path.GetTempPath()),

            OSKind.MacOS => new AiPaths(
                home,
                System.IO.Path.Combine(home, "Library", "Application Support"),
                System.IO.Path.Combine(home, "Library", "Caches"),
                Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } tmp ? tmp : "/tmp"),

            _ => new AiPaths(
                Linux.XdgPaths.Home,
                System.IO.Path.Combine(Linux.XdgPaths.Home, ".config"),
                Linux.XdgPaths.CacheHome,
                "/tmp"),
        };
    }
}

/// <summary>
/// Every place osXos knows an AI assistant leaves something, and the two operations
/// over it: turning a target into the rows the Review stage shows, and deleting rows
/// the user has approved.
///
/// The map is deliberately a literal list rather than a search. A tool that went
/// looking for anything AI-shaped under your profile would eventually find something
/// it should not have, and the Review stage would be the only thing standing between
/// that guess and your files.
///
/// Nothing here ever names a credential, a configuration file, a hand-written
/// instruction file, a skill or a memory store. <c>AiFootprintTests.Nothing_in_the_map
/// _can_reach_a_credential_or_a_setting</c> pins that list the same way the elevation
/// allow-list is pinned, so a path cannot join it quietly.
/// </summary>
public static class AiFootprint
{
    /// <summary>Every target for this OS, grouped by product in display order.</summary>
    public static IReadOnlyList<AiTarget> For(OSKind os) => For(os, AiPaths.Current(os));

    public static IReadOnlyList<AiTarget> For(OSKind os, AiPaths paths)
    {
        var targets = new List<AiTarget>();
        targets.AddRange(ClaudeCode(paths));
        targets.AddRange(Codex(paths));
        targets.AddRange(Gemini(paths));
        targets.AddRange(os switch
        {
            OSKind.Windows => WindowsOnly(paths),
            OSKind.MacOS => MacOnly(paths),
            _ => LinuxOnly(paths),
        });
        return targets;
    }

    /// <summary>The targets one job is allowed to touch.</summary>
    public static IReadOnlyList<AiTarget> For(OSKind os, AiPaths paths, IReadOnlyCollection<AiSpill> spills) =>
        For(os, paths).Where(t => spills.Contains(t.Spill)).ToList();

    // ---------------------------------------------------------------- products --

    /// <summary>
    /// Claude Code, in <c>~/.claude</c> on all three platforms. Kept out of the map
    /// entirely: <c>.credentials.json</c>, <c>settings.json</c>, <c>skills/</c>, and
    /// <c>~/.claude.json</c> alongside it.
    /// </summary>
    static IEnumerable<AiTarget> ClaudeCode(AiPaths p)
    {
        var root = Path.Combine(p.Home, ".claude");
        const string product = "Claude Code";

        yield return new(product, "cache", Path.Combine(root, "cache"), AiSpill.Scratch);
        yield return new(product, "pasted-content cache", Path.Combine(root, "paste-cache"), AiSpill.Scratch);
        yield return new(product, "shell snapshots", Path.Combine(root, "shell-snapshots"), AiSpill.Scratch);
        yield return new(product, "session environments", Path.Combine(root, "session-env"), AiSpill.Scratch);
        yield return new(product, "background jobs", Path.Combine(root, "jobs"), AiSpill.Scratch);
        yield return new(product, "daemon state", Path.Combine(root, "daemon"), AiSpill.Scratch);
        yield return new(product, "daemon log", Path.Combine(root, "daemon.log"), AiSpill.Scratch);
        yield return new(product, "downloads", Path.Combine(root, "downloads"), AiSpill.Scratch);
        yield return new(product, "file-edit undo history", Path.Combine(root, "file-history"), AiSpill.Scratch);
        yield return new(product, "usage statistics cache", Path.Combine(root, "stats-cache.json"), AiSpill.Scratch);
        yield return new(product, "last update result", Path.Combine(root, ".last-update-result.json"), AiSpill.Scratch);

        // One row per project, and memory/ inside each one is never handed to the sweep.
        yield return new(product, "transcripts", Path.Combine(root, "projects"), AiSpill.History)
        {
            PreservedChild = "memory",
        };
        yield return new(product, "session index", Path.Combine(root, "sessions"), AiSpill.History);
        yield return new(product, "prompt history", Path.Combine(root, "history.jsonl"), AiSpill.History);

        yield return new(product, "settings backups", Path.Combine(root, "backups"), AiSpill.Residue);
        yield return new(product, "installed plugins", Path.Combine(root, "plugins"), AiSpill.Residue);
    }

    /// <summary>
    /// Codex, in <c>~/.codex</c>. Kept out of the map entirely: <c>auth.json</c>,
    /// <c>config.toml</c>, <c>.sandbox-secrets</c>, <c>skills/</c> and
    /// <c>memories_1.sqlite</c>.
    /// </summary>
    static IEnumerable<AiTarget> Codex(AiPaths p)
    {
        var root = Path.Combine(p.Home, ".codex");
        const string product = "Codex";

        yield return new(product, "scratch", Path.Combine(root, ".tmp"), AiSpill.Scratch);
        yield return new(product, "temp", Path.Combine(root, "tmp"), AiSpill.Scratch);
        yield return new(product, "cache", Path.Combine(root, "cache"), AiSpill.Scratch);
        yield return new(product, "sandbox", Path.Combine(root, ".sandbox"), AiSpill.Scratch);
        yield return new(product, "sandbox binaries", Path.Combine(root, ".sandbox-bin"), AiSpill.Scratch);
        yield return new(product, "sandbox migration", Path.Combine(root, ".sandbox_migration"), AiSpill.Scratch);
        yield return new(product, "node REPL history", Path.Combine(root, "node_repl"), AiSpill.Scratch);
        yield return new(product, "thread writer locks", Path.Combine(root, "thread-writer-locks"), AiSpill.Scratch);
        yield return new(product, "log database", Path.Combine(root, "logs_2.sqlite"), AiSpill.Scratch);
        yield return new(product, "log database (shared memory)", Path.Combine(root, "logs_2.sqlite-shm"), AiSpill.Scratch);
        yield return new(product, "log database (write-ahead log)", Path.Combine(root, "logs_2.sqlite-wal"), AiSpill.Scratch);

        yield return new(product, "session transcripts", Path.Combine(root, "sessions"), AiSpill.History);
        yield return new(product, "thread history database", Path.Combine(root, "thread_history_1.sqlite"), AiSpill.History);
        yield return new(product, "dictation history", Path.Combine(root, "dictation-history"), AiSpill.History);

        yield return new(product, "installed plugins", Path.Combine(root, "plugins"), AiSpill.Residue);
        yield return new(product, "generated images", Path.Combine(root, "generated_images"), AiSpill.Residue);
        yield return new(product, "generated visualizations", Path.Combine(root, "visualizations"), AiSpill.Residue);
        yield return new(product, "computer-use recordings", Path.Combine(root, "computer-use"), AiSpill.Residue);
        yield return new(product, "sqlite working files", Path.Combine(root, "sqlite"), AiSpill.Residue);
        yield return new(product, "state database", Path.Combine(root, "state_5.sqlite"), AiSpill.Residue);
        yield return new(product, "queue database", Path.Combine(root, "queue_1.sqlite"), AiSpill.Residue);
        yield return new(product, "goals database", Path.Combine(root, "goals_1.sqlite"), AiSpill.Residue);
        yield return new(product, "previous global state", Path.Combine(root, ".codex-global-state.json.bak"), AiSpill.Residue);
    }

    /// <summary>
    /// The Gemini CLI and the Antigravity data it hosts, in <c>~/.gemini</c>. Kept out
    /// of the map entirely: <c>oauth_creds.json</c>, <c>google_accounts.json</c>,
    /// <c>settings.json</c>, <c>trustedFolders.json</c> and <c>GEMINI.md</c>.
    /// </summary>
    static IEnumerable<AiTarget> Gemini(AiPaths p)
    {
        var root = Path.Combine(p.Home, ".gemini");
        const string product = "Gemini & Antigravity";

        yield return new(product, "temp", Path.Combine(root, "tmp"), AiSpill.Scratch);

        yield return new(product, "prompt history", Path.Combine(root, "history"), AiSpill.History);

        yield return new(product, "settings backup", Path.Combine(root, "antigravity-backup"), AiSpill.Residue);
        yield return new(product, "browser profile", Path.Combine(root, "antigravity-browser-profile"), AiSpill.Residue);
        yield return new(product, "replaced settings file", Path.Combine(root, "settings.json.orig"), AiSpill.Residue);
        yield return new(product, "session state", Path.Combine(root, "state.json"), AiSpill.Residue);
        yield return new(product, "IDE extensions", Path.Combine(p.Home, ".antigravity", "extensions"), AiSpill.Residue);
        yield return new(product, "IDE extensions (second install)", Path.Combine(p.Home, ".antigravity-ide", "extensions"), AiSpill.Residue);
    }

    // --------------------------------------------------------------- platforms --

    static IEnumerable<AiTarget> WindowsOnly(AiPaths p)
    {
        // The per-session scratchpad directories every Claude Code run creates.
        yield return new("Claude Code", "session scratchpads",
            Path.Combine(p.Temp, "claude"), AiSpill.Scratch);

        foreach (var t in Electron("Claude Desktop", Path.Combine(p.AppSupport, "Claude"))) yield return t;
        foreach (var t in Electron("Claude Desktop", Path.Combine(p.AppSupport, "Claude-3p"))) yield return t;

        yield return new("Claude Desktop", "embedded Claude Code builds",
            Path.Combine(p.AppSupport, "Claude", "claude-code"), AiSpill.Scratch);
        yield return new("Claude Desktop", "embedded Claude Code sessions",
            Path.Combine(p.AppSupport, "Claude", "claude-code-sessions"), AiSpill.History);
        yield return new("Claude Desktop", "logs",
            Path.Combine(p.AppCache, "Claude", "logs"), AiSpill.Scratch);

        // Squirrel keeps the .nupkg it installed every version from. Routinely the
        // largest single thing on this list, and re-downloaded on demand. The
        // app-<version> folders beside it are deliberately not here: one of them is
        // the copy currently running and osXos will not guess which.
        yield return new("Claude Desktop", "installer package cache",
            Path.Combine(p.AppCache, "AnthropicClaude", "packages"), AiSpill.Scratch);

        foreach (var t in Electron("Antigravity", Path.Combine(p.AppSupport, "Antigravity"))) yield return t;
        foreach (var t in Electron("Antigravity IDE", Path.Combine(p.AppSupport, "Antigravity IDE"))) yield return t;
    }

    static IEnumerable<AiTarget> MacOnly(AiPaths p)
    {
        yield return new("Claude Code", "session scratchpads",
            Path.Combine(p.Temp, "claude"), AiSpill.Scratch);

        foreach (var t in Electron("Claude Desktop", Path.Combine(p.AppSupport, "Claude"))) yield return t;

        yield return new("Claude Desktop", "embedded Claude Code builds",
            Path.Combine(p.AppSupport, "Claude", "claude-code"), AiSpill.Scratch);
        yield return new("Claude Desktop", "embedded Claude Code sessions",
            Path.Combine(p.AppSupport, "Claude", "claude-code-sessions"), AiSpill.History);
        yield return new("Claude Desktop", "cache",
            Path.Combine(p.AppCache, "com.anthropic.claudefordesktop"), AiSpill.Scratch);
        yield return new("Claude Desktop", "logs",
            Path.Combine(p.Home, "Library", "Logs", "Claude"), AiSpill.Scratch);

        foreach (var t in Electron("Antigravity", Path.Combine(p.AppSupport, "Antigravity"))) yield return t;
    }

    static IEnumerable<AiTarget> LinuxOnly(AiPaths p)
    {
        yield return new("Claude Code", "session scratchpads",
            Path.Combine(p.Temp, "claude"), AiSpill.Scratch);
        yield return new("Claude Code", "cache",
            Path.Combine(p.AppCache, "claude"), AiSpill.Scratch);

        foreach (var t in Electron("Claude Desktop", Path.Combine(p.AppSupport, "Claude"))) yield return t;

        yield return new("Claude Desktop", "embedded Claude Code builds",
            Path.Combine(p.AppSupport, "Claude", "claude-code"), AiSpill.Scratch);
        yield return new("Claude Desktop", "embedded Claude Code sessions",
            Path.Combine(p.AppSupport, "Claude", "claude-code-sessions"), AiSpill.History);

        foreach (var t in Electron("Antigravity", Path.Combine(p.AppSupport, "Antigravity"))) yield return t;
    }

    /// <summary>
    /// The cache folders every Electron application keeps under its data directory,
    /// named the same way in all of them. Named individually rather than sweeping the
    /// directory, because the same directory holds <c>Preferences</c>, the login
    /// cookie jar under <c>Network</c>, and the application's own configuration.
    /// </summary>
    static IEnumerable<AiTarget> Electron(string product, string dir)
    {
        string[] scratch =
        {
            "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache",
            "blob_storage", "logs", "Crashpad", "CachedData", "CachedExtensionVSIXs",
            "CachedProfilesData", "CachedConfigurations", "Service Worker",
            "VideoDecodeStats", "Shared Dictionary",
        };
        foreach (var name in scratch)
            yield return new(product, name, Path.Combine(dir, name), AiSpill.Scratch);

        yield return new(product, "editor backups", Path.Combine(dir, "Backups"), AiSpill.Residue);
        yield return new(product, "workspace storage", Path.Combine(dir, "User", "workspaceStorage"), AiSpill.Residue);
        yield return new(product, "local file history", Path.Combine(dir, "User", "History"), AiSpill.Residue);
    }

    // ------------------------------------------------------------- inspect/run --

    /// <summary>
    /// What one target actually amounts to on this machine, as Review rows. A target
    /// that is not there yields nothing — a machine that never installed Codex is not
    /// an error, it is the normal case for two of the three products.
    /// </summary>
    public static IEnumerable<PreviewItem> Rows(AiTarget target, CancellationToken ct = default)
    {
        if (target.PreservedChild is null)
        {
            var size = Measure(target.Path, ct);
            if (size is null) yield break;
            yield return new PreviewItem($"{target.Product} · {target.Label}", target.Path, size);
            yield break;
        }

        string[] subdirs;
        try { subdirs = Directory.GetDirectories(target.Path); }
        catch { yield break; }

        foreach (var sub in subdirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            long total = 0;
            var any = false;
            foreach (var child in Deletable(sub, target.PreservedChild))
            {
                any = true;
                total += Measure(child, ct) ?? 0;
            }

            if (!any) continue;
            yield return new PreviewItem(
                $"{target.Product} · {target.Label} — {Path.GetFileName(sub)}", sub, total);
        }
    }

    /// <summary>
    /// Deletes rows the user approved on Review. Rows belonging to a target with a
    /// <see cref="AiTarget.PreservedChild"/> are expanded into their deletable
    /// children first, so the preserved folder is never handed to the sweep at all
    /// rather than being handed over and skipped.
    /// </summary>
    public static SweepOutcome Delete(
        IReadOnlyList<AiTarget> targets,
        IEnumerable<PreviewItem> items,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default)
    {
        var expanded = new List<PreviewItem>();

        foreach (var item in items)
        {
            var path = item.Detail;
            if (string.IsNullOrEmpty(path)) continue;

            var preserved = PreservedChildFor(targets, path);
            if (preserved is null)
            {
                expanded.Add(item);
                continue;
            }

            foreach (var child in Deletable(path, preserved))
                expanded.Add(new PreviewItem(item.Label, child, Measure(child, ct)));
        }

        return FileSweep.Delete(expanded, progress, ct);
    }

    /// <summary>
    /// The name to preserve inside <paramref name="path"/>, when it is a row produced
    /// by a container target. Matched by asking whether the row's parent is that
    /// target's directory, which is what makes the lookup a pure function of the map
    /// and the path rather than state left over from the inspection.
    /// </summary>
    static string? PreservedChildFor(IReadOnlyList<AiTarget> targets, string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (parent is null) return null;

        return targets.FirstOrDefault(t =>
            t.PreservedChild is not null &&
            string.Equals(
                Path.TrimEndingDirectorySeparator(t.Path), parent,
                StringComparison.OrdinalIgnoreCase))?.PreservedChild;
    }

    /// <summary>Everything directly inside <paramref name="dir"/> except one name.</summary>
    static IEnumerable<string> Deletable(string dir, string preserved)
    {
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(dir); }
        catch { yield break; }

        foreach (var entry in entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFileName(entry), preserved, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return entry;
        }
    }

    /// <summary>
    /// The size of a file or a directory tree, or null when the path is not there at
    /// all. A file is measured with one stat rather than a walk, which matters:
    /// expanding a project folder measures a few dozen transcripts, and walking each
    /// one as though it might be a tree would double the cost of the whole scan.
    /// </summary>
    static long? Measure(string path, CancellationToken ct)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
        }
        catch
        {
            return null;
        }

        return Directory.Exists(path) ? FileSweep.SizeOf(path, ct) : null;
    }
}
