namespace OsXos.Tools.Linux;

/// <summary>
/// Removes Flatpak runtimes and extensions nothing depends on any more — what is left
/// behind each time an app moves to a newer runtime. The per-user installation only:
/// the system-wide one needs polkit, and the Review stage names the command for it
/// rather than osXos prompting on flatpak's behalf.
/// </summary>
public sealed class FlatpakUnusedTool : ITool
{
    public static readonly ShellCommand ListUserRuntimes =
        new("flatpak", "list", "--user", "--runtime", "--columns=application,branch");

    public static readonly ShellCommand RemoveUnused =
        new("flatpak", "uninstall", "--user", "--unused", "--noninteractive", "-y");

    public static readonly ShellCommand SystemEquivalent = new("flatpak", "uninstall", "--unused");

    readonly IProcessRunner _runner;

    public FlatpakUnusedTool(IProcessRunner runner) => _runner = runner;

    public string Id => "linux.flatpak-unused";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Packages;
    public string Name => "Remove Unused Flatpak Runtimes";
    public string Summary => "Uninstall the Flatpak runtimes and extensions no installed app uses any more.";
    public string IconKey => "IconPackages";
    public string? Warning => "Flatpak decides what is unused: anything an installed app still needs stays. Apps you have uninstalled cannot be launched either way.";
    public bool IsDestructive => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read your Flatpak runtimes",
            "flatpak list --user --runtime. Runtimes are the shared platforms apps are built on — GNOME, KDE, freedesktop — and each app update can move to a newer one, leaving the old one installed."),
        new("Let Flatpak remove what nothing uses",
            "flatpak uninstall --user --unused. Flatpak itself works out which runtimes and extensions no installed app depends on; osXos does not guess."),
        new("Your installation only",
            "Runtimes installed system-wide need polkit to remove. Rather than prompting on Flatpak's behalf, the Review stage shows the command to run in a terminal for those."),
    };

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("flatpak"))
            return ToolPreview.Blocked("Flatpak is not installed, so there are no runtimes to clean up.");

        var listed = await _runner.RunAsync(ListUserRuntimes, ct).ConfigureAwait(false);
        var runtimes = listed.Ok
            ? listed.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length
            : 0;

        var items = new[]
        {
            new PreviewItem("Runtimes in your installation", runtimes.ToString("N0")),
            new PreviewItem("Will run", RemoveUnused.Display),
            new PreviewItem("For the system installation, run yourself", SystemEquivalent.Display),
        };
        return new ToolPreview(items, runtimes == 0
            ? "No runtimes in your own installation; Flatpak will confirm there is nothing unused."
            : $"Flatpak will remove whichever of your {runtimes} runtimes nothing uses.");
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var outcome = await _runner.RunAsync(RemoveUnused, ct).ConfigureAwait(false);
        if (!outcome.Ok)
            return ToolResult.Failure("Flatpak could not remove unused runtimes",
                RemoveUnused.Display, $"exit code {outcome.ExitCode}", outcome.Message);

        var said = outcome.Message.Length > 0 ? outcome.Message : "Done.";
        return ToolResult.Success("Unused Flatpak runtimes removed",
            RemoveUnused.Display, said,
            $"System-wide runtimes were not touched; `{SystemEquivalent.Display}` in a terminal handles those.");
    }
}

/// <summary>
/// Clears the downloaded package files the system package manager keeps after
/// installing them — apt, dnf, pacman or zypper, whichever this machine uses. The
/// cache is root-owned, so this is one of the two tools in osXos that elevate: one
/// named command, through polkit, never osXos itself.
/// </summary>
public sealed class PackageCacheTool : ITool
{
    /// <summary>One package manager: how to recognise it, where it caches, how to clean.</summary>
    public sealed record Manager(string Name, string Binary, string CacheDir, ShellCommand Clean);

    public static readonly IReadOnlyList<Manager> Managers = new[]
    {
        new Manager("apt", "apt-get", "/var/cache/apt/archives", new ShellCommand("apt-get", "clean")),
        new Manager("dnf", "dnf", "/var/cache/dnf", new ShellCommand("dnf", "clean", "packages")),
        new Manager("pacman", "pacman", "/var/cache/pacman/pkg", new ShellCommand("pacman", "-Sc", "--noconfirm")),
        new Manager("zypper", "zypper", "/var/cache/zypp/packages", new ShellCommand("zypper", "--non-interactive", "clean", "--all")),
    };

    readonly IProcessRunner _runner;
    readonly IElevationService _elevation;
    readonly Func<string, long?> _sizeOf;

    public PackageCacheTool(IProcessRunner runner, IElevationService elevation)
        : this(runner, elevation, dir => Directory.Exists(dir) ? FileSweep.SizeOf(dir) : null) { }

    public PackageCacheTool(IProcessRunner runner, IElevationService elevation, Func<string, long?> sizeOf)
    {
        _runner = runner;
        _elevation = elevation;
        _sizeOf = sizeOf;
    }

    public string Id => "linux.package-cache";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Packages;
    public string Name => "Clean Package Cache";
    public string Summary => "Delete the package files apt, dnf, pacman or zypper kept after installing them.";
    public string IconKey => "IconDownload";
    public string? Warning => "Needs administrator rights — polkit asks for your password once. Do not run this while the package manager is installing or updating.";
    public bool IsDestructive => true;

    /// <summary>The system package cache is owned by root, whichever manager keeps it.</summary>
    public bool RequiresElevation => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Find this machine's package manager",
            "apt on Debian and Ubuntu, dnf on Fedora, pacman on Arch, zypper on openSUSE — whichever is installed, first match wins."),
        new("Measure its download cache",
            "/var/cache/apt/archives, /var/cache/dnf, /var/cache/pacman/pkg or /var/cache/zypp/packages. Readable without rights, so the size on Review is the real figure."),
        new("Clean it with the manager's own command",
            "apt-get clean, dnf clean packages, pacman -Sc (which keeps the packages for what is currently installed) or zypper clean --all. The cache is owned by root, so polkit asks for your password for that one command; osXos itself never runs as root."),
        new("Nothing installed changes",
            "Only downloaded package files go. Installed software is untouched, and a package you reinstall is simply downloaded again."),
    };

    Manager? Detect() => Managers.FirstOrDefault(m => _runner.Exists(m.Binary));

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (Detect() is not { } manager)
            return Task.FromResult(ToolPreview.Blocked(
                "None of apt, dnf, pacman or zypper is installed, so there is no package cache osXos knows how to clean."));

        var size = _sizeOf(manager.CacheDir);
        var items = new[]
        {
            new PreviewItem($"{manager.Name} download cache", manager.CacheDir, size),
            new PreviewItem("Will run, elevated", manager.Clean.Display),
        };

        var summary = size is { } s
            ? $"{FileSweep.FormatBytes(s)} in the {manager.Name} cache · needs administrator rights"
            : $"The {manager.Name} cache · needs administrator rights";
        return Task.FromResult(new ToolPreview(items, summary) { NeedsElevation = true });
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        if (Detect() is not { } manager)
            return ToolResult.Failure("No package manager found", "Nothing was changed.");

        if (!_elevation.CanElevate)
            return ToolResult.Failure("Administrator rights are not available",
                "pkexec was not found, so osXos cannot ask for them.",
                "Run this in a terminal instead:", "sudo " + manager.Clean.Display);

        var before = _sizeOf(manager.CacheDir);
        progress?.Report(new ToolProgress(0, 0, "Waiting for administrator approval..."));
        var outcome = await _elevation.RunElevatedAsync(manager.Clean, ct).ConfigureAwait(false);

        // pkexec exits 126 when the authentication dialog is dismissed.
        if (outcome.ExitCode == 126)
            return ToolResult.Failure("Cancelled", "Administrator rights were declined, so nothing was changed.");

        if (!outcome.Ok)
            return ToolResult.Failure("Could not clean the package cache",
                manager.Clean.Display, $"exit code {outcome.ExitCode}", outcome.Message);

        var after = _sizeOf(manager.CacheDir);
        var lines = new List<string> { manager.Clean.Display };
        if (before is { } b && after is { } a)
            lines.Add($"Reclaimed {FileSweep.FormatBytes(Math.Max(0, b - a))}.");
        lines.Add("Installed software was not touched.");

        return ToolResult.Success($"The {manager.Name} package cache is clean", lines.ToArray());
    }
}

/// <summary>
/// Restarts the user's sound server — PipeWire with WirePlumber on current desktops,
/// PulseAudio on older ones. The fix for most "no sound" and "device missing"
/// problems, without logging out and without administrator rights.
/// </summary>
public sealed class RestartAudioTool : ITool
{
    /// <summary>The user units that make up the sound stack, in restart order.</summary>
    public static readonly string[] Units =
        { "pipewire.service", "pipewire-pulse.service", "wireplumber.service", "pulseaudio.service" };

    public static ShellCommand IsActive(string unit) => new("systemctl", "--user", "is-active", unit);

    public static ShellCommand Restart(IEnumerable<string> units) =>
        new("systemctl", new[] { "--user", "restart" }.Concat(units).ToArray());

    readonly IProcessRunner _runner;

    public RestartAudioTool(IProcessRunner runner) => _runner = runner;

    public string Id => "linux.restart-audio";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Services;
    public string Name => "Restart Audio";
    public string Summary => "Restart PipeWire or PulseAudio to bring back missing sound or devices, without logging out.";
    public string IconKey => "IconRefresh";
    public string? Warning => "Sound cuts out for a moment. Apps that are playing may need to be paused and resumed to reconnect.";
    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Find which sound server is running",
            "systemctl --user is-active for pipewire, pipewire-pulse, wireplumber and pulseaudio. Only the ones actually running are restarted — nothing is started that was not already there."),
        new("Restart them together",
            "One systemctl --user restart, so PipeWire and its session manager come back in step. These are your own user services: no administrator rights, and no other account's sound is affected."),
        new("Apps reconnect",
            "Most apps pick the new server up by themselves. One that stays silent usually needs its playback paused and resumed, or restarting."),
    };

    async Task<List<string>> ActiveUnits(CancellationToken ct)
    {
        var active = new List<string>();
        foreach (var unit in Units)
        {
            var outcome = await _runner.RunAsync(IsActive(unit), ct).ConfigureAwait(false);
            if (outcome.StdOut.Trim() == "active") active.Add(unit);
        }
        return active;
    }

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("systemctl"))
            return ToolPreview.Blocked("systemctl was not found. osXos restarts audio through systemd user services, which this system does not use.");

        var active = await ActiveUnits(ct).ConfigureAwait(false);
        if (active.Count == 0)
            return ToolPreview.Blocked(
                "Neither PipeWire nor PulseAudio is running as a user service here, so there is nothing to restart. " +
                "If there is no sound at all, the sound server may not be starting — your desktop's sound settings are the place to look.");

        var items = active.Select(u => new PreviewItem(u, "running — will restart"))
            .Append(new PreviewItem("Will run", Restart(active).Display))
            .ToList();
        return new ToolPreview(items, $"Will restart {string.Join(", ", active.Select(u => u.Replace(".service", "")))}.");
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var active = await ActiveUnits(ct).ConfigureAwait(false);
        if (active.Count == 0) return ToolResult.Failure("Nothing to restart", "No sound server is running as a user service.");

        var command = Restart(active);
        var outcome = await _runner.RunAsync(command, ct).ConfigureAwait(false);
        return outcome.Ok
            ? ToolResult.Success("Audio restarted", command.Display,
                "If an app is still silent, pause and resume its playback.")
            : ToolResult.Failure("Could not restart audio", command.Display,
                $"exit code {outcome.ExitCode}", outcome.Message);
    }
}

/// <summary>
/// Lists the systemd services that have failed, yours and the system's — the first
/// thing to look at when something that should be running is not. Read-only.
/// </summary>
public sealed class FailedServicesTool : ITool
{
    public static readonly ShellCommand UserFailed = new("systemctl", "--user", "--failed", "--no-legend", "--plain");
    public static readonly ShellCommand SystemFailed = new("systemctl", "--failed", "--no-legend", "--plain");

    readonly IProcessRunner _runner;

    public FailedServicesTool(IProcessRunner runner) => _runner = runner;

    public string Id => "linux.failed-services";
    public OSKind Platform => OSKind.Linux;
    public ToolCategory Category => ToolCategory.Services;
    public string Name => "Failed Services Report";
    public string Summary => "List the systemd services that have failed, for your session and the system — read-only.";
    public string IconKey => "IconWarning";
    public string? Warning => null;
    public bool IsDestructive => false;

    /// <summary>A report: reads, and changes nothing.</summary>
    public bool IsReadOnly => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Ask systemd what has failed",
            "systemctl --user --failed for the services in your own session, and systemctl --failed for the system's. Reading either needs no administrator rights."),
        new("List each one",
            "Every failed unit with systemd's own description of it. A clean system lists nothing, and the Review stage says so."),
        new("Change nothing",
            "This only reads. To see why a unit failed, run journalctl -u <unit> (add --user for your own); to clear the failed state once it is fixed, systemctl reset-failed."),
    };

    /// <summary>A --plain --no-legend line: unit, load, active, sub, then the description.</summary>
    public static IReadOnlyList<(string Unit, string Description)> Parse(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 1)
            .Select(p => (p[0], p.Length == 5 ? p[4] : ""))
            .ToList();

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("systemctl"))
            return ToolPreview.Blocked("systemctl was not found — this system does not use systemd, so there are no units to report.");

        var user = Parse((await _runner.RunAsync(UserFailed, ct).ConfigureAwait(false)).StdOut);
        var system = Parse((await _runner.RunAsync(SystemFailed, ct).ConfigureAwait(false)).StdOut);

        if (user.Count == 0 && system.Count == 0)
            return ToolPreview.Blocked("No failed services — everything systemd was asked to run is running or finished cleanly.");

        var items = user.Select(u => new PreviewItem($"{u.Unit}  (your session)", u.Description))
            .Concat(system.Select(s => new PreviewItem($"{s.Unit}  (system)", s.Description)))
            .ToList();

        return new ToolPreview(items,
            $"{items.Count} failed unit{(items.Count == 1 ? "" : "s")}. Read-only — running this changes nothing.");
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var lines = preview.Items
            .Select(i => $"{i.Label} — {i.Detail}")
            .Concat(new[]
            {
                "Nothing was changed.",
                "To see why a unit failed: journalctl -u <unit> (add --user for your own session's).",
            })
            .ToArray();
        return Task.FromResult(ToolResult.Success(preview.Summary.Split(". ")[0], lines));
    }
}
