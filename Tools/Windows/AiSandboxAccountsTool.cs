using System.Text;

namespace OsXos.Tools.Windows;

/// <summary>
/// Removes the local Windows accounts AI coding tools create to sandbox the commands
/// they run — Codex's CodexSandboxOffline and CodexSandboxOnline, and the group they
/// belong to — with any profile folder they left.
///
/// Like the rest of the AI Assistants category it works from a fixed list of known
/// names, never a search: an account is somebody's, and a tool that guessed which
/// ones looked machine-made would eventually guess a person. Deleting an account
/// needs administrator rights, so this is one elevated script, and the accounts are
/// listed again afterwards to report what is actually left.
/// </summary>
public sealed class AiSandboxAccountsTool : ITool, IHasState
{
    /// <summary>One known sandbox account or group, and the product that makes it.</summary>
    public sealed record Known(string Name, bool IsGroup, string Product);

    /// <summary>
    /// Every account osXos knows an AI tool creates. Deliberately a list: a name joins
    /// it only once the product that makes it is identified, and a test pins it.
    /// </summary>
    public static readonly IReadOnlyList<Known> KnownAccounts = new Known[]
    {
        new("CodexSandboxOffline", false, "Codex"),
        new("CodexSandboxOnline", false, "Codex"),
        new("CodexSandboxUsers", true, "Codex"),
    };

    /// <summary>One account or group found on this machine.</summary>
    public sealed record Found(Known Known, string? Sid, bool Enabled, string? LastLogon, string? ProfilePath);

    /// <summary>
    /// Lists the known names that exist, with each user's SID, state, last sign-in and
    /// profile folder. Get-LocalUser, Get-LocalGroup and Win32_UserProfile all answer
    /// without administrator rights.
    /// </summary>
    public static ShellCommand Query { get; } = new("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", BuildQuery());

    static string BuildQuery()
    {
        var users = string.Join(",", KnownAccounts.Where(k => !k.IsGroup).Select(k => $"'{k.Name}'"));
        var groups = string.Join(",", KnownAccounts.Where(k => k.IsGroup).Select(k => $"'{k.Name}'"));
        return
            $"foreach ($n in @({users})) {{ $u = Get-LocalUser -Name $n -ErrorAction SilentlyContinue; " +
            "if ($u) { $p = Get-CimInstance Win32_UserProfile -Filter \"SID='$($u.SID.Value)'\" -ErrorAction SilentlyContinue; " +
            "\"user|$($u.Name)|$($u.SID.Value)|$($u.Enabled)|$($u.LastLogon)|$($p.LocalPath)\" } }; " +
            $"foreach ($n in @({groups})) {{ if (Get-LocalGroup -Name $n -ErrorAction SilentlyContinue) {{ \"group|$n\" }} }}";
    }

    readonly IProcessRunner _runner;
    readonly IElevationService _elevation;
    readonly Func<bool> _productRunning;
    readonly Func<string> _tempFile;

    public AiSandboxAccountsTool(IProcessRunner runner, IElevationService elevation)
        : this(runner, elevation,
            () => System.Diagnostics.Process.GetProcessesByName("codex").Length > 0,
            () => Path.Combine(Path.GetTempPath(), $"osxos-sandbox-accounts-{Guid.NewGuid():N}.ps1")) { }

    public AiSandboxAccountsTool(IProcessRunner runner, IElevationService elevation, Func<bool> productRunning, Func<string> tempFile)
    {
        _runner = runner;
        _elevation = elevation;
        _productRunning = productRunning;
        _tempFile = tempFile;
    }

    public string Id => "windows.ai-sandbox-accounts";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.AI;
    public string Name => "Remove AI Sandbox Accounts";
    public string Summary => "Delete the local Windows accounts Codex creates to sandbox its commands, and the group they belong to.";
    public string IconKey => "IconPrivacy";

    public string? Warning =>
        "Close Codex first — a sandbox it is using right now stops working. Codex creates these accounts again the next time its sandbox starts, " +
        "so this is for when you have stopped using it, or want its sandbox set up from scratch.";

    public bool IsDestructive => true;

    /// <summary>Deleting a Windows account always needs administrator rights.</summary>
    public bool RequiresElevation => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Look for the accounts by name",
            "Codex's Windows sandbox runs commands as separate local accounts — CodexSandboxOffline, and CodexSandboxOnline when a command needs the network — in a group called CodexSandboxUsers. osXos looks for exactly those names and no others; it never guesses which accounts look machine-made."),
        new("Show what is there",
            "Each account found is listed with whether it is enabled, when it last signed in, and its profile folder if it has one. Listing accounts needs no administrator rights, and nothing changes while you read."),
        new("Delete them as administrator",
            "One elevated PowerShell, so Windows asks for administrator rights once: each account's profile is removed the supported way (its folder and its ProfileList entry together), then the account, then the group. Your own account and every other account are untouched."),
        new("Codex will make them again",
            "These are Codex's to manage. If you keep using Codex, its sandbox recreates them the next time it starts — which is also how to reset a sandbox that has stopped working. Afterwards the accounts are listed again, and the result says what is actually gone."),
    };

    // ---- reading ----

    public static IReadOnlyList<Found> Parse(string stdout)
    {
        var found = new List<Found>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p[0] == "user" && p.Length >= 6 && Lookup(p[1], false) is { } user)
                found.Add(new Found(user, Empty(p[2]), p[3].Equals("True", StringComparison.OrdinalIgnoreCase), Empty(p[4]), Empty(p[5])));
            else if (p[0] == "group" && p.Length >= 2 && Lookup(p[1], true) is { } group)
                found.Add(new Found(group, null, true, null, null));
        }
        return found;

        static string? Empty(string s) => s.Trim().Length == 0 ? null : s.Trim();
    }

    /// <summary>Only names on the known list are ever accepted, whatever the query printed.</summary>
    static Known? Lookup(string name, bool isGroup) =>
        KnownAccounts.FirstOrDefault(k => k.IsGroup == isGroup && k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    async Task<IReadOnlyList<Found>?> FindAsync(CancellationToken ct)
    {
        var outcome = await _runner.RunAsync(Query, ct).ConfigureAwait(false);
        return outcome.Ok ? Parse(outcome.StdOut) : null;
    }

    public async Task<ToolState> ReadStateAsync(CancellationToken ct)
    {
        var found = await FindAsync(ct).ConfigureAwait(false);
        if (found is null) return new ToolState("Unknown", StateTone.Unavailable, "Windows did not list its local accounts.");
        var users = found.Count(f => !f.Known.IsGroup);
        return found.Count == 0
            ? new ToolState("None", StateTone.Off, "No AI sandbox account exists on this PC.")
            : new ToolState($"{users} account{(users == 1 ? "" : "s")}", StateTone.On,
                string.Join(", ", found.Select(f => f.Known.Name)));
    }

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var found = await FindAsync(ct).ConfigureAwait(false);
        if (found is null)
            return ToolPreview.Blocked("Windows did not list its local accounts, so osXos cannot tell which sandbox accounts exist.");
        if (found.Count == 0)
            return ToolPreview.Blocked("Nothing to remove — no account an AI tool creates exists on this PC.");

        var items = found.Select(f => new PreviewItem(
            $"{f.Known.Product} · {f.Known.Name}{(f.Known.IsGroup ? " (group)" : "")}",
            f.Known.IsGroup
                ? "local group — removed after its accounts"
                : $"{(f.Enabled ? "enabled" : "disabled")} · last sign-in {f.LastLogon ?? "never"} · " +
                  (f.ProfilePath is { } path ? $"profile {path} will be removed" : "no profile folder")))
            .ToList();

        if (_productRunning())
            items.Insert(0, new PreviewItem("Codex is running", "Close it first — the sandbox it is using now would stop working."));

        var users = found.Count(f => !f.Known.IsGroup);
        var state = new ToolState($"{users} account{(users == 1 ? "" : "s")}", StateTone.On);
        return new ToolPreview(items,
            $"{users} sandbox account{(users == 1 ? "" : "s")}{(found.Any(f => f.Known.IsGroup) ? " and their group" : "")} · needs administrator rights")
        {
            NeedsElevation = true,
            State = state,
        };
    }

    // ---- removing ----

    static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>The elevated script: profiles, then accounts, then groups. Public for the tests.</summary>
    public static string RemovalScript(IEnumerable<Found> found)
    {
        var list = found.ToList();
        var sb = new StringBuilder("$ErrorActionPreference = 'Continue'\n");
        foreach (var f in list.Where(f => !f.Known.IsGroup))
        {
            if (f.Sid is { } sid)
                sb.Append($"Get-CimInstance Win32_UserProfile -Filter \"SID='{sid.Replace("'", "")}'\" -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction Continue\n");
            sb.Append($"Remove-LocalUser -Name {Q(f.Known.Name)} -ErrorAction Continue\n");
        }
        foreach (var f in list.Where(f => f.Known.IsGroup))
            sb.Append($"Remove-LocalGroup -Name {Q(f.Known.Name)} -ErrorAction Continue\n");
        return sb.ToString();
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var found = await FindAsync(ct).ConfigureAwait(false);
        if (found is null || found.Count == 0)
            return ToolResult.Failure("Nothing to remove", "No AI sandbox account exists on this PC any more.");

        if (!_elevation.CanElevate)
            return ToolResult.Failure("Administrator rights are not available", "Deleting a Windows account needs them.");

        var file = _tempFile();
        try
        {
            await File.WriteAllTextAsync(file, RemovalScript(found), new UTF8Encoding(true), ct).ConfigureAwait(false);
            progress?.Report(new ToolProgress(0, 0, "Waiting for administrator approval..."));
            var outcome = await _elevation.RunElevatedAsync(WinUtilScriptTool.PowerShellFile(file), ct).ConfigureAwait(false);
            if (outcome.ExitCode == 1223)
                return ToolResult.Failure("Cancelled", "Administrator rights were declined, so nothing was changed.");
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }

        // Ask Windows again rather than trust the exit code.
        var left = await FindAsync(ct).ConfigureAwait(false) ?? found;
        var removed = found.Where(f => !left.Any(l => l.Known.Name.Equals(f.Known.Name, StringComparison.OrdinalIgnoreCase))).ToList();

        var lines = new List<string>();
        lines.AddRange(removed.Select(r => $"Removed {r.Known.Name}{(r.Known.IsGroup ? " (group)" : "")}{(r.ProfilePath is { } p ? $" and its profile {p}" : "")}."));
        lines.AddRange(left.Select(l => $"Still there: {l.Known.Name} — it may be in use; close Codex and run this again."));
        lines.Add("Codex creates these again the next time its sandbox starts.");

        return removed.Count == 0
            ? ToolResult.Failure("No account could be removed", lines.ToArray())
            : ToolResult.Success($"Removed {removed.Count} of {found.Count} sandbox account{(found.Count == 1 ? "" : "s")} and groups", lines.ToArray());
    }
}
