namespace OsXos.Tools.Ai;

/// <summary>
/// What kind of thing an AI assistant left behind. The three tools in the AI
/// Assistants category are each a filter over this — which is the whole reason the
/// distinction is a value and not a comment.
/// </summary>
public enum AiSpill
{
    /// <summary>
    /// Written for one session and never read again: per-session scratchpads, temp
    /// folders, shell snapshots, pasted clipboard images, throwaway git indexes. Not
    /// even a cache — nothing is slower for its absence — so it is the one kind of
    /// spill a tool can take without the user giving anything up.
    /// </summary>
    Temp,

    /// <summary>
    /// Reproducible by definition: caches, logs, sandbox binaries, downloaded
    /// installer payloads. Deleting any of it costs a slower next launch and nothing
    /// else.
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
/// One place an AI assistant leaves things, and which kind of spill it is. Everything
/// about how it is measured and deleted is a <see cref="Location"/>'s; the spill is
/// the only thing the three AI tools add, because it is what they filter on.
/// </summary>
public sealed record AiTarget(string Product, string Label, string Path, AiSpill Spill)
    : Location(Product, Label, Path);

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
    public static IReadOnlyList<AiTarget> For(OSKind os) => For(os, ProfileRoots.Current(os));

    public static IReadOnlyList<AiTarget> For(OSKind os, ProfileRoots paths)
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
    public static IReadOnlyList<AiTarget> For(OSKind os, ProfileRoots paths, IReadOnlyCollection<AiSpill> spills) =>
        For(os, paths).Where(t => spills.Contains(t.Spill)).ToList();

    // ---------------------------------------------------------------- products --

    /// <summary>
    /// Claude Code, in <c>~/.claude</c> on all three platforms. Kept out of the map
    /// entirely: <c>.credentials.json</c>, <c>settings.json</c>, <c>skills/</c>, and
    /// <c>~/.claude.json</c> alongside it.
    /// </summary>
    static IEnumerable<AiTarget> ClaudeCode(ProfileRoots p)
    {
        var root = Path.Combine(p.Home, ".claude");
        const string product = "Claude Code";

        yield return new(product, "cache", Path.Combine(root, "cache"), AiSpill.Scratch);
        yield return new(product, "pasted-content cache", Path.Combine(root, "paste-cache"), AiSpill.Scratch);
        yield return new(product, "shell snapshots", Path.Combine(root, "shell-snapshots"), AiSpill.Temp);
        yield return new(product, "session environments", Path.Combine(root, "session-env"), AiSpill.Temp);
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
    static IEnumerable<AiTarget> Codex(ProfileRoots p)
    {
        var root = Path.Combine(p.Home, ".codex");
        const string product = "Codex";

        yield return new(product, "scratch", Path.Combine(root, ".tmp"), AiSpill.Temp);
        yield return new(product, "temp", Path.Combine(root, "tmp"), AiSpill.Temp);

        // Loose in the system temp folder rather than under ~/.codex, and never
        // cleaned up: every image pasted into a prompt, and the throwaway git index
        // each working-tree snapshot is built with.
        yield return new(product, "pasted clipboard images",
            Path.Combine(p.Temp, "codex-clipboard-*.png"), AiSpill.Temp) { IsPattern = true };
        yield return new(product, "snapshot git indexes",
            Path.Combine(p.Temp, "codex-index-*"), AiSpill.Temp) { IsPattern = true };
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
    static IEnumerable<AiTarget> Gemini(ProfileRoots p)
    {
        var root = Path.Combine(p.Home, ".gemini");
        const string product = "Gemini & Antigravity";

        // Despite the name, ~/.gemini/tmp is where the Gemini CLI keeps each project's
        // saved chats and prompt log (tmp/<project>/chats, logs.json). It is history,
        // and a cache clear must not take it.
        yield return new(product, "project chats and logs", Path.Combine(root, "tmp"), AiSpill.History);
        yield return new(product, "prompt history", Path.Combine(root, "history"), AiSpill.History);

        yield return new(product, "settings backup", Path.Combine(root, "antigravity-backup"), AiSpill.Residue);
        yield return new(product, "browser profile", Path.Combine(root, "antigravity-browser-profile"), AiSpill.Residue);
        yield return new(product, "replaced settings file", Path.Combine(root, "settings.json.orig"), AiSpill.Residue);
        yield return new(product, "session state", Path.Combine(root, "state.json"), AiSpill.Residue);
        yield return new(product, "IDE extensions", Path.Combine(p.Home, ".antigravity", "extensions"), AiSpill.Residue);
        yield return new(product, "IDE extensions (second install)", Path.Combine(p.Home, ".antigravity-ide", "extensions"), AiSpill.Residue);
    }

    // --------------------------------------------------------------- platforms --

    static IEnumerable<AiTarget> WindowsOnly(ProfileRoots p)
    {
        // The per-session scratchpad directories every Claude Code run creates.
        yield return new("Claude Code", "session scratchpads",
            Path.Combine(p.Temp, "claude"), AiSpill.Temp);

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

    static IEnumerable<AiTarget> MacOnly(ProfileRoots p)
    {
        yield return new("Claude Code", "session scratchpads",
            Path.Combine(p.Temp, "claude"), AiSpill.Temp);

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

    static IEnumerable<AiTarget> LinuxOnly(ProfileRoots p)
    {
        yield return new("Claude Code", "session scratchpads",
            Path.Combine(p.Temp, "claude"), AiSpill.Temp);
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

    /// <summary>What one target actually amounts to on this machine, as Review rows.</summary>
    public static IEnumerable<PreviewItem> Rows(AiTarget target, CancellationToken ct = default) =>
        LocationSweep.Rows(target, ct);

    /// <summary>
    /// Deletes rows the user approved on Review. Rows belonging to a project folder
    /// are expanded into their deletable children first, so <c>memory/</c> is never
    /// handed to the sweep at all rather than being handed over and skipped.
    /// </summary>
    public static SweepOutcome Delete(
        IReadOnlyList<AiTarget> targets,
        IEnumerable<PreviewItem> items,
        IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default) =>
        LocationSweep.Delete(targets, items, progress, ct);
}
