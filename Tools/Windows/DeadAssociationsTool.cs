namespace OsXos.Tools.Windows;

/// <summary>
/// Removes the file-association leftovers of programs that are no longer there — the
/// reason "Open with" offers an app uninstalled a year ago, or a file type still shows
/// a dead program's icon. Unlike Rebuild Open With Lists, which throws the whole
/// per-extension history away, this takes only the entries that point at a program
/// that is gone, and leaves every working choice exactly as it was.
///
/// Per-user only. Leftovers under HKEY_LOCAL_MACHINE need administrator rights to
/// remove and belong to whichever installer left them; the Review stage counts them
/// so they are not invisible, and leaves them alone.
/// </summary>
public sealed class DeadAssociationsTool : ITool, IHasState
{
    public const string FileExts = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
    public const string Classes = @"Software\Classes";
    const string AppPaths = @"Software\Microsoft\Windows\CurrentVersion\App Paths";

    /// <summary>Every Store package installed for this user, by full name (Name_Version_Arch__PublisherId).</summary>
    public const string Packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    /// <summary>The row Open with list entries that are not program names at all are grouped under.</summary>
    public const string JunkLabel = "Entries that are not programs";

    /// <summary>Keys directly under Classes that are infrastructure, never a program's ProgID.</summary>
    static readonly HashSet<string> NotProgIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLSID", "Interface", "TypeLib", "AppID", "Applications", "Local Settings", "WOW6432Node",
        "SystemFileAssociations", "*", "Directory", "Folder", "Drive", "AllFilesystemObjects",
        "DesktopBackground", "Unknown", "MIME", "Extensions", "ActivatableClasses", "PackagedCom",
    };

    readonly IRegistryAccess _registry;
    readonly IUserRegistry _user;
    readonly Action _notifyShell;
    readonly Func<string, bool> _fileExists;
    readonly Func<string, bool> _rootExists;
    readonly Func<string, string?> _findOnPath;

    public DeadAssociationsTool(IRegistryAccess registry, IUserRegistry user, Action notifyShell)
        : this(registry, user, notifyShell, File.Exists,
            path => Path.GetPathRoot(path) is { Length: > 0 } root && Directory.Exists(root),
            FindOnPath) { }

    /// <summary>Everything the tool asks of the file system, supplied — for the tests.</summary>
    public DeadAssociationsTool(
        IRegistryAccess registry, IUserRegistry user, Action notifyShell,
        Func<string, bool> fileExists, Func<string, bool> rootExists, Func<string, string?> findOnPath)
    {
        _registry = registry;
        _user = user;
        _notifyShell = notifyShell;
        _fileExists = fileExists;
        _rootExists = rootExists;
        _findOnPath = findOnPath;
    }

    public string Id => "windows.dead-associations";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Shell;
    public string Name => "Remove Dead App Associations";
    public string Summary => "Take uninstalled programs out of Open with and file types, and leave every working association as it is.";
    public string IconKey => "IconTrash";
    public string? Warning => "Only references to programs that are gone are removed. A file type whose default pointed at one opens with Windows' choice again until you pick another.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Look everywhere your account records a program for a file type",
            @"Each extension's Open with list and remembered program types under Explorer\FileExts, your chosen default (UserChoice), your account's own program registrations under Software\Classes\Applications, its own file types (ProgIDs), and each extension's list of offered types. All under HKEY_CURRENT_USER."),
        new("Find the dead ones, and only those",
            "A reference is dead when the program it names has a full path that no longer exists, or when the program or file type it names is no longer registered anywhere — not for you, not machine-wide, not on the PATH. A program on a drive that is not connected right now is never counted as dead. A Store app counts as gone only when Windows no longer lists its package as installed for you, and Open with entries that are not program names at all — stray strings some apps leave there — are offered for removal as one group."),
        new("Show them grouped by program",
            "The Review stage lists each dead program once, with every place that still points at it. Working programs, and your choices for them, do not appear at all — that is the difference from Rebuild Open With Lists, which clears everything."),
        new("Remove those references, then tell Explorer",
            "Each one is deleted precisely — one value, or one key — and an Open with list's order is kept for the programs that remain. A default that pointed at a dead program is cleared, so Windows offers its own choice until you pick again. Explorer is notified that associations changed; no restart, and no administrator rights."),
    };

    // ---- model ----

    enum RefKind { ListValue, ProgIdValue, UserChoice, AppKey, ProgIdKey, DefaultValue }

    /// <summary>One thing to delete, and the program it is why.</summary>
    sealed record Reference(string Program, string Why, string Where, RefKind Kind, string Key, string Name);

    enum Liveness { Alive, Dead, Unregistered }

    sealed record Verdict(Liveness State, string? MissingPath = null);

    // ---- resolving programs ----

    /// <summary>
    /// The program a command line starts, expanded and unquoted — or null when it does
    /// not name one by full path (a bare "rundll32.exe", say), which this tool will not
    /// judge.
    /// </summary>
    public static string? ExecutableOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = Environment.ExpandEnvironmentVariables(command.Trim());

        string candidate;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end < 0) return null;
            candidate = text[1..end];
        }
        else
        {
            // Unquoted paths with spaces: take the shortest prefix ending in .exe.
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exe > 0 ? text[..(exe + 4)] : text.Split(' ')[0];
        }

        return Path.IsPathFullyQualified(candidate) ? candidate : null;
    }

    Verdict FromPath(string? exe)
    {
        if (exe is null) return new Verdict(Liveness.Alive); // not a full path; not ours to judge
        if (_fileExists(exe)) return new Verdict(Liveness.Alive);
        // A drive that is not connected right now proves nothing about the program.
        return _rootExists(exe) ? new Verdict(Liveness.Dead, exe) : new Verdict(Liveness.Alive);
    }

    /// <summary>The command a ProgID or Applications key runs: its open verb, else its default or first verb.</summary>
    string? CommandOf(RegHive hive, string key)
    {
        string? Read(string verb) => _registry.GetValue(hive, $@"{key}\shell\{verb}\command", "")?.ToString();

        var command = Read("open");
        if (command is not null) return command;
        if (_registry.GetValue(hive, $@"{key}\shell", "")?.ToString() is { Length: > 0 } preferred && Read(preferred) is { } p) return p;
        var verbs = _registry.SubKeyNames(hive, $@"{key}\shell");
        return verbs.Count > 0 ? Read(verbs[0]) : null;
    }

    static bool IsPackaged(string progId) =>
        progId.StartsWith("AppX", StringComparison.OrdinalIgnoreCase) ||
        progId.StartsWith("AppXP", StringComparison.OrdinalIgnoreCase) ||
        progId.Contains('_') && progId.Contains("!", StringComparison.Ordinal);

    HashSet<string>? _installedFamilies;

    /// <summary>
    /// Store app references carry a family name (Name_PublisherId) before the "!";
    /// the package repository lists installed packages by full name. Same family
    /// means the same app, whatever version is installed now.
    /// </summary>
    Verdict Packaged(string aumid)
    {
        _installedFamilies ??= _registry.SubKeyNames(RegHive.CurrentUser, Packages)
            .Select(FamilyOf)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var family = aumid.Split('!')[0];
        return _installedFamilies.Count == 0 || _installedFamilies.Contains(family)
            ? new Verdict(Liveness.Alive) // an empty list means we could not read it, not that nothing is installed
            : new Verdict(Liveness.Unregistered);
    }

    /// <summary>"Name_1.2.3.0_x64__pubid" → "Name_pubid".</summary>
    public static string? FamilyOf(string packageFullName)
    {
        var first = packageFullName.IndexOf('_');
        var last = packageFullName.LastIndexOf("__", StringComparison.Ordinal);
        return first > 0 && last > first ? packageFullName[..first] + "_" + packageFullName[(last + 2)..] : null;
    }

    readonly Dictionary<string, Verdict> _progIdCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Verdict> _appCache = new(StringComparer.OrdinalIgnoreCase);

    Verdict ProgId(string progId)
    {
        if (_progIdCache.TryGetValue(progId, out var cached)) return cached;
        Verdict verdict;

        if (IsPackaged(progId))
            verdict = new Verdict(Liveness.Alive);
        else if (progId.StartsWith(@"Applications\", StringComparison.OrdinalIgnoreCase))
            verdict = App(progId[@"Applications\".Length..]);
        else
        {
            var key = $@"{Classes}\{progId}";
            var inUser = _registry.KeyExists(RegHive.CurrentUser, key);
            var inMachine = _registry.KeyExists(RegHive.LocalMachine, key);
            if (!inUser && !inMachine)
                verdict = new Verdict(Liveness.Unregistered);
            else
            {
                // Alive if either registration starts a program that exists.
                var verdicts = new List<Verdict>();
                if (inUser) verdicts.Add(FromPath(ExecutableOf(CommandOf(RegHive.CurrentUser, key))));
                if (inMachine) verdicts.Add(FromPath(ExecutableOf(CommandOf(RegHive.LocalMachine, key))));
                verdict = verdicts.FirstOrDefault(v => v.State == Liveness.Alive) ?? verdicts[0];
            }
        }

        return _progIdCache[progId] = verdict;
    }

    Verdict App(string exeName)
    {
        if (_appCache.TryGetValue(exeName, out var cached)) return cached;

        var found = new List<Verdict>();
        foreach (var hive in new[] { RegHive.CurrentUser, RegHive.LocalMachine })
        {
            var appKey = $@"{Classes}\Applications\{exeName}";
            if (_registry.KeyExists(hive, appKey))
            {
                var exe = ExecutableOf(CommandOf(hive, appKey));
                if (exe is not null) found.Add(FromPath(exe));
            }
            if (_registry.GetValue(hive, $@"{AppPaths}\{exeName}", "")?.ToString() is { Length: > 0 } appPath)
                found.Add(FromPath(ExecutableOf(appPath.StartsWith('"') ? appPath : $"\"{appPath}\"")));
        }

        Verdict verdict;
        if (found.Any(v => v.State == Liveness.Alive))
            verdict = new Verdict(Liveness.Alive);
        else if (found.Count > 0)
            verdict = found[0];
        else
            verdict = _findOnPath(exeName) is not null ? new Verdict(Liveness.Alive) : new Verdict(Liveness.Unregistered);

        return _appCache[exeName] = verdict;
    }

    static string Why(Verdict v) =>
        v.State == Liveness.Dead ? $"{v.MissingPath} is gone" : "no longer registered anywhere";

    // ---- scanning ----

    List<Reference> Scan()
    {
        _progIdCache.Clear();
        _appCache.Clear();
        _installedFamilies = null;
        var found = new List<Reference>();

        void Flag(string program, Verdict v, string where, RefKind kind, string key, string name)
        {
            if (v.State == Liveness.Alive) return;
            found.Add(new Reference(program, Why(v), where, kind, key, name));
        }

        // 1–3: Explorer's per-extension records.
        foreach (var ext in _registry.SubKeyNames(RegHive.CurrentUser, FileExts))
        {
            var root = $@"{FileExts}\{ext}";

            var list = $@"{root}\OpenWithList";
            foreach (var name in _registry.ValueNames(RegHive.CurrentUser, list))
            {
                if (name.Equals("MRUList", StringComparison.OrdinalIgnoreCase)) continue;
                if (_registry.GetValue(RegHive.CurrentUser, list, name)?.ToString() is not { Length: > 0 } entry) continue;
                var where = $"{ext} · Open with list";

                if (entry.Contains('!'))
                    Flag(entry, Packaged(entry), where, RefKind.ListValue, list, name);
                else if (entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    // A bare name Windows cannot resolve is not shown in Open with anyway;
                    // only one registered at a path that has gone is a visible leftover.
                    var verdict = App(entry);
                    if (verdict.State == Liveness.Dead)
                        Flag(entry, verdict, where, RefKind.ListValue, list, name);
                }
                else if (!entry.StartsWith("Microsoft.AutoGenerated.", StringComparison.OrdinalIgnoreCase))
                    found.Add(new Reference(JunkLabel, "not program names; some app wrote them here", where, RefKind.ListValue, list, name));
            }

            var progIds = $@"{root}\OpenWithProgids";
            foreach (var progId in _registry.ValueNames(RegHive.CurrentUser, progIds))
                if (progId.Length > 0)
                    Flag(progId, ProgId(progId), $"{ext} · remembered program types", RefKind.ProgIdValue, progIds, progId);

            var choice = $@"{root}\UserChoice";
            if (_registry.GetValue(RegHive.CurrentUser, choice, "ProgId")?.ToString() is { Length: > 0 } chosen)
                Flag(chosen, ProgId(chosen), $"{ext} · your default", RefKind.UserChoice, root, "UserChoice");
        }

        // 4: your own program registrations.
        foreach (var exe in _registry.SubKeyNames(RegHive.CurrentUser, $@"{Classes}\Applications"))
        {
            var verdict = FromPath(ExecutableOf(CommandOf(RegHive.CurrentUser, $@"{Classes}\Applications\{exe}")));
            Flag(exe, verdict, "your program registration", RefKind.AppKey, $@"{Classes}\Applications", exe);
        }

        // 5–6: your own file types, and what each extension offers.
        foreach (var name in _registry.SubKeyNames(RegHive.CurrentUser, Classes))
        {
            if (name.StartsWith('.'))
            {
                var extKey = $@"{Classes}\{name}";
                if (_registry.GetValue(RegHive.CurrentUser, extKey, "")?.ToString() is { Length: > 0 } progId)
                    Flag(progId, ProgId(progId), $"{name} · your file type", RefKind.DefaultValue, extKey, "");
                var offered = $@"{extKey}\OpenWithProgids";
                foreach (var p in _registry.ValueNames(RegHive.CurrentUser, offered))
                    if (p.Length > 0)
                        Flag(p, ProgId(p), $"{name} · offered program types", RefKind.ProgIdValue, offered, p);
                continue;
            }

            if (NotProgIds.Contains(name) || IsPackaged(name)) continue;
            var key = $@"{Classes}\{name}";
            if (CommandOf(RegHive.CurrentUser, key) is not { } command) continue;
            var verdict = FromPath(ExecutableOf(command));
            Flag(name, verdict, "your file type registration", RefKind.ProgIdKey, Classes, name);
        }

        return found;
    }

    /// <summary>
    /// How many machine-wide program registrations are dead too. Counted so they are
    /// not invisible; never removed, since that needs administrator rights.
    /// </summary>
    int MachineWideDead()
    {
        var count = 0;
        foreach (var exe in _registry.SubKeyNames(RegHive.LocalMachine, $@"{Classes}\Applications"))
            if (FromPath(ExecutableOf(CommandOf(RegHive.LocalMachine, $@"{Classes}\Applications\{exe}"))).State == Liveness.Dead)
                count++;
        return count;
    }

    // ---- state / inspect / run ----

    public Task<ToolState> ReadStateAsync(CancellationToken ct)
    {
        var programs = Scan().Select(r => r.Program).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return Task.FromResult(programs == 0
            ? new ToolState("Clean", StateTone.Off, "No association points at a program that is gone.")
            : new ToolState($"{programs} dead", StateTone.Partial, $"{programs} program{(programs == 1 ? "" : "s")} that no longer exist still appear in file associations."));
    }

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var refs = Scan();
        var machine = MachineWideDead();

        if (refs.Count == 0)
            return Task.FromResult(ToolPreview.Blocked(
                "Nothing to remove — every program your file associations name still exists." +
                (machine > 0 ? $" ({machine} dead machine-wide registration{(machine == 1 ? "" : "s")} need administrator rights and are left alone.)" : "")));

        // One row per dead program; the detail lists every place it is still named.
        // The label carries the program so Run can find its references again.
        var items = refs
            .GroupBy(r => r.Program, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PreviewItem(
                $"{g.Key} — {g.First().Why}",
                string.Join("; ", g.Select(r => r.Where).Distinct())))
            .ToList();

        var summary = $"{items.Count} dead program{(items.Count == 1 ? "" : "s")} · {refs.Count} reference{(refs.Count == 1 ? "" : "s")} to remove";
        if (machine > 0) summary += $" · {machine} machine-wide left alone";
        return Task.FromResult(new ToolPreview(items, summary) { State = new ToolState($"{items.Count} dead", StateTone.Partial) });
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        // Only programs Review showed; the label starts with the program's name.
        var shown = preview.Items.Select(i => i.Label.Split(" — ")[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var refs = Scan().Where(r => shown.Contains(r.Program)).ToList();

        int removed = 0, failed = 0, done = 0;
        var problems = new List<string>();

        foreach (var r in refs)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ToolProgress(++done, refs.Count, r.Program));

            var ok = r.Kind switch
            {
                RefKind.ListValue => RemoveFromList(r.Key, r.Name),
                RefKind.ProgIdValue or RefKind.DefaultValue => _registry.DeleteValue(RegHive.CurrentUser, r.Key, r.Name),
                RefKind.UserChoice or RefKind.AppKey or RefKind.ProgIdKey => _registry.DeleteSubKeyTree(RegHive.CurrentUser, r.Key, r.Name),
                _ => false,
            };

            if (ok) removed++;
            else
            {
                failed++;
                if (problems.Count < 5) problems.Add($"Could not remove {r.Where} ({r.Program}).");
            }
        }

        if (removed > 0) _notifyShell();

        var lines = new List<string>
        {
            $"Removed {removed} of {refs.Count} reference{(refs.Count == 1 ? "" : "s")} to {shown.Count} dead program{(shown.Count == 1 ? "" : "s")}.",
        };
        lines.AddRange(problems);
        lines.Add("Explorer was told associations changed; Open with lists update straight away.");

        return Task.FromResult(removed > 0
            ? ToolResult.Success($"Removed {shown.Count} dead program{(shown.Count == 1 ? "" : "s")} from file associations", lines.ToArray())
            : ToolResult.Failure("Nothing could be removed", lines.ToArray()));
    }

    /// <summary>
    /// Removes one entry from an Open with list and takes its letter out of MRUList,
    /// so the remaining programs keep their order.
    /// </summary>
    bool RemoveFromList(string key, string name)
    {
        if (!_registry.DeleteValue(RegHive.CurrentUser, key, name)) return false;
        if (_registry.GetValue(RegHive.CurrentUser, key, "MRUList")?.ToString() is { } mru && name.Length == 1)
        {
            try { _user.SetString(key, "MRUList", mru.Replace(name, "", StringComparison.OrdinalIgnoreCase)); }
            catch { /* the order is cosmetic; the dead entry is already gone */ }
        }
        return true;
    }

    /// <summary>Where Windows itself would find a bare program name: System32, the Windows folder, then PATH.</summary>
    static string? FindOnPath(string exeName)
    {
        var dirs = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        foreach (var dir in dirs.Where(d => d.Length > 0))
        {
            try
            {
                var full = Path.Combine(dir, exeName);
                if (File.Exists(full)) return full;
            }
            catch { /* a malformed PATH entry */ }
        }
        return null;
    }
}
