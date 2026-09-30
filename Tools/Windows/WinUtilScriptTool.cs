using System.Text;

namespace OsXos.Tools.Windows;

/// <summary>What shape a WinUtil entry has, which decides whether it has a state and how Run behaves.</summary>
public enum ScriptKind
{
    /// <summary>Registry values and/or services that can be read back: a two-way toggle with a state.</summary>
    Toggle,

    /// <summary>A one-shot job — reset the network, create a restore point. No state, runs every time.</summary>
    Action,

    /// <summary>Windows optional features to turn on. State comes from Windows' feature list.</summary>
    Feature,

    /// <summary>Opens a Windows panel. Nothing changes; no administrator rights.</summary>
    Launcher,
}

/// <summary>One service a WinUtil entry sets, and what running it again restores.</summary>
public sealed record ServiceChange(string Name, string Startup, string Original);

/// <summary>
/// One WinUtil entry that needs more than registry values: a PowerShell script,
/// service start types, optional features, or a panel to open. Scripts are WinUtil's
/// own, word for word, with a few of its helpers supplied by <see cref="WinUtilScriptTool.Prelude"/>.
/// </summary>
public sealed record WinUtilScript(
    string Slug,
    ToolCategory Category,
    string Name,
    string Summary,
    string IconKey,
    ScriptKind Kind,
    IReadOnlyList<ToolStep> Steps)
{
    public IReadOnlyList<TweakValue> Registry { get; init; } = Array.Empty<TweakValue>();
    public IReadOnlyList<ServiceChange> Services { get; init; } = Array.Empty<ServiceChange>();
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>PowerShell run when the tool applies. For a launcher, the command that opens the panel.</summary>
    public string? Script { get; init; }

    /// <summary>PowerShell run when a toggle is undone, after its values are restored.</summary>
    public string? UndoScript { get; init; }

    public bool Destructive { get; init; }
    public string? Caution { get; init; }
    public bool NeedsRestart { get; init; }

    /// <summary>A two-state preference ("Taskbar Centered Icons"): applied reads as on rather than as a thing switched off.</summary>
    public bool IsPreference { get; init; }

    public string AppliedLabel { get; init; } = "Applied";
    public string NotAppliedLabel { get; init; } = "Not applied";
    public string? Source { get; init; } = "Chris Titus Tech's WinUtil";

    /// <summary>Everything except opening a panel changes the machine in ways that need administrator rights.</summary>
    public bool Elevated => Kind != ScriptKind.Launcher;
}

/// <summary>
/// Runs a WinUtil entry the way WinUtil does — as administrator, in PowerShell —
/// but inside osXos's contract: Review shows every value, service, feature and the
/// full script before anything runs, the run is one elevated PowerShell with one
/// UAC prompt, and what it printed comes back on the Result stage. Toggles read
/// their state back afterwards; features ask Windows' own feature list.
/// </summary>
public sealed class WinUtilScriptTool : ITool, IHasState
{
    readonly WinUtilScript _entry;
    readonly IRegistryAccess _registry;
    readonly IProcessRunner _runner;
    readonly IElevationService _elevation;
    readonly Func<string> _tempBase;

    public WinUtilScriptTool(
        WinUtilScript entry, IRegistryAccess registry, IProcessRunner runner, IElevationService elevation,
        Func<string>? tempBase = null)
    {
        _entry = entry;
        _registry = registry;
        _runner = runner;
        _elevation = elevation;
        _tempBase = tempBase ?? (() => Path.Combine(Path.GetTempPath(), $"osxos-{entry.Slug}-{Guid.NewGuid():N}"));
    }

    public WinUtilScript Entry => _entry;

    public string Id => "windows." + _entry.Slug;
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => _entry.Category;
    public string Name => _entry.Name;
    public string Summary => _entry.Summary;
    public string IconKey => _entry.IconKey;
    public bool IsDestructive => _entry.Destructive;
    public bool RequiresElevation => _entry.Elevated;
    public IReadOnlyList<ToolStep> Steps => _entry.Steps;

    public string? Warning
    {
        get
        {
            var parts = new List<string>();
            if (_entry.Caution is { } c) parts.Add(c);
            if (_entry.Kind == ScriptKind.Action && _entry.Destructive) parts.Add("This cannot be undone from osXos.");
            if (_entry.NeedsRestart) parts.Add("Takes effect after a restart.");
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    // ---- state ----

    static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    string? CurrentValue(TweakValue v) => _registry.GetValue(v.Hive, v.KeyPath, v.Name) switch
    {
        null => null,
        int i => unchecked((uint)i).ToString(),
        long l => unchecked((ulong)l).ToString(),
        var o => o.ToString(),
    };

    /// <summary>A service's start type as WinUtil names it, read from its registry key.</summary>
    string? CurrentStartup(string service)
    {
        var key = $@"SYSTEM\CurrentControlSet\Services\{service}";
        if (_registry.GetValue(RegHive.LocalMachine, key, "Start") is not int start) return null;
        var delayed = _registry.GetValue(RegHive.LocalMachine, key, "DelayedAutostart") is 1;
        return start switch
        {
            2 => delayed ? "AutomaticDelayedStart" : "Automatic",
            3 => "Manual",
            4 => "Disabled",
            _ => start.ToString(),
        };
    }

    /// <summary>How many of a toggle's parts are in place, out of how many that exist on this machine.</summary>
    (int Set, int Total) ToggleProgress()
    {
        var set = _entry.Registry.Count(v => Same(CurrentValue(v), v.Value));
        var total = _entry.Registry.Count;
        foreach (var s in _entry.Services)
        {
            var now = CurrentStartup(s.Name);
            if (now is null) continue; // not installed here; nothing to set
            total++;
            if (Same(now, s.Startup)) set++;
        }
        return (set, total);
    }

    bool ToggleApplied
    {
        get
        {
            var (set, total) = ToggleProgress();
            return total > 0 && set == total;
        }
    }

    public static ShellCommand FeatureQuery(IEnumerable<string> features) => new("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
        "Get-CimInstance -ClassName Win32_OptionalFeature | Where-Object { @(" +
        string.Join(",", features.Select(f => $"'{f.Replace("'", "''")}'")) +
        ") -contains $_.Name } | ForEach-Object { \"$($_.Name)=$($_.InstallState)\" }");

    /// <summary>Feature name → Win32_OptionalFeature InstallState (1 enabled, 2 disabled, 3 absent).</summary>
    async Task<Dictionary<string, string>?> FeatureStates(CancellationToken ct)
    {
        var outcome = await _runner.RunAsync(FeatureQuery(_entry.Features), ct).ConfigureAwait(false);
        if (!outcome.Ok) return null;
        return outcome.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ToolState> ReadStateAsync(CancellationToken ct)
    {
        switch (_entry.Kind)
        {
            case ScriptKind.Toggle:
            {
                var (set, total) = ToggleProgress();
                if (total == 0) return new ToolState("Not on this PC", StateTone.Unavailable, "None of the services this sets are installed here.");
                var (onTone, offTone) = _entry.IsPreference ? (StateTone.Off, StateTone.On) : (StateTone.On, StateTone.Off);
                return set == 0 ? new ToolState(_entry.NotAppliedLabel, onTone, "Not applied.")
                    : set == total ? new ToolState(_entry.AppliedLabel, offTone, "Applied.")
                    : new ToolState("Partly applied", StateTone.Partial, $"{set} of {total} changes are in place.");
            }
            case ScriptKind.Feature when _entry.Features.Count > 0:
            {
                var states = await FeatureStates(ct).ConfigureAwait(false);
                if (states is null) return new ToolState("Unknown", StateTone.Unavailable, "Windows did not say which features are installed.");
                var present = _entry.Features.Where(states.ContainsKey).ToList();
                if (present.Count == 0) return new ToolState("Not on this PC", StateTone.Unavailable, "This edition of Windows does not offer it.");
                var enabled = present.Count(f => states[f] == "1");
                return enabled == present.Count ? new ToolState("Enabled", StateTone.On)
                    : enabled == 0 ? new ToolState("Not enabled", StateTone.Off)
                    : new ToolState("Partly enabled", StateTone.Partial, $"{enabled} of {present.Count} features are on.");
            }
            case ScriptKind.Launcher:
                return new ToolState("Opens a panel", StateTone.Off);
            default:
                return new ToolState("Runs on demand", StateTone.Off, "A one-off job: it has no on or off state.");
        }
    }

    // ---- inspect ----

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var state = await ReadStateAsync(ct).ConfigureAwait(false);
        var undo = _entry.Kind == ScriptKind.Toggle && ToggleApplied;
        var items = new List<PreviewItem>();

        foreach (var v in _entry.Registry)
        {
            var now = CurrentValue(v) ?? "not set";
            var target = undo ? v.Original ?? "removed" : v.Value;
            items.Add(new PreviewItem(v.Label, $"{v.Display} = {now} → {target}"));
        }

        foreach (var s in _entry.Services)
        {
            var now = CurrentStartup(s.Name);
            items.Add(new PreviewItem($"Service {s.Name}",
                now is null ? "not installed on this PC — skipped" : $"{now} → {(undo ? s.Original : s.Startup)}"));
        }

        foreach (var f in _entry.Features)
            items.Add(new PreviewItem($"Windows feature {f}", "turn on (Enable-WindowsOptionalFeature -All -NoRestart)"));

        var script = undo ? _entry.UndoScript : _entry.Script;
        if (!string.IsNullOrWhiteSpace(script))
            items.Add(new PreviewItem(
                _entry.Kind == ScriptKind.Launcher ? "Will run" : undo ? "Then runs WinUtil's undo script" : "Then runs WinUtil's script",
                script.Trim()));

        if (_entry.Kind == ScriptKind.Feature && state is { Label: "Enabled" })
            return new ToolPreview(items, "Already enabled — there is nothing to turn on.", "Already enabled on this PC.") { State = state };

        var summary = _entry.Kind switch
        {
            ScriptKind.Launcher => "Opens the panel. Nothing on the system changes.",
            ScriptKind.Toggle => undo ? "Applied. Will undo it — the original values and start types come back." : "Will apply it.",
            ScriptKind.Feature => "Will turn the feature on. Windows may need a restart to finish.",
            _ => "Will run it.",
        };
        if (_entry.Elevated) summary += " · needs administrator rights";

        return new ToolPreview(items, summary) { NeedsElevation = _entry.Elevated, State = state };
    }

    // ---- run ----

    /// <summary>
    /// Stand-ins for the WinUtil helpers its scripts call — logging, progress, the
    /// background runner and Explorer refresh — so each script runs unmodified.
    /// </summary>
    public const string Prelude = """
        $ErrorActionPreference = 'Continue'
        $ProgressPreference = 'SilentlyContinue'
        function Write-WinUtilLog { param($Component, $Message, $Level) if ($Message) { Write-Host "[$Component] $Message" } }
        function Step-WinUtilJob { param($Status, $Percent, $State) if ($Status) { Write-Host $Status } }
        function Show-WinUtilMessage { param($Title, $Message) if ($Message) { Write-Host $Message } }
        function Invoke-WPFRunspace { param($ScriptBlock, $ArgumentList) & $ScriptBlock @ArgumentList }
        function Invoke-WinUtilExplorerUpdate {
            param([string]$action = 'refresh')
            if ($action -eq 'restart') { taskkill.exe /F /IM explorer.exe | Out-Null; Start-Process explorer.exe }
            else {
                if (-not ([System.Management.Automation.PSTypeName]'OsXosBroadcast').Type) {
                    Add-Type -Namespace '' -Name OsXosBroadcast -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern System.IntPtr SendMessageTimeout(System.IntPtr h, uint m, System.IntPtr w, string l, uint f, uint t, out System.IntPtr r);'
                }
                $r = [System.IntPtr]::Zero
                [void][OsXosBroadcast]::SendMessageTimeout([System.IntPtr]0xffff, 0x1A, [System.IntPtr]::Zero, 'ImmersiveColorSet', 2, 100, [ref]$r)
            }
        }
        """;

    static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>The whole PowerShell file for one direction. Public so tests — and Review readers — can see it.</summary>
    public string BuildScript(bool undo, string logPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Start-Transcript -Path {Q(logPath)} -Force | Out-Null");
        sb.AppendLine(Prelude);
        sb.AppendLine("$osxosFailed = $false");
        sb.AppendLine("try {");

        foreach (var v in _entry.Registry)
        {
            var path = $"Registry::{v.HiveName}\\{v.KeyPath}";
            var target = undo ? v.Original : v.Value;
            if (target is null)
            {
                sb.AppendLine($"  Remove-ItemProperty -Path {Q(path)} -Name {Q(v.Name)} -ErrorAction SilentlyContinue");
            }
            else
            {
                var type = v.Kind switch { RegValueKind.DWord => "DWord", RegValueKind.QWord => "QWord", _ => "String" };
                sb.AppendLine($"  if (-not (Test-Path {Q(path)})) {{ New-Item -Path {Q(path)} -Force | Out-Null }}");
                sb.AppendLine($"  Set-ItemProperty -Path {Q(path)} -Name {Q(v.Name)} -Type {type} -Value {Q(target)} -Force");
            }
        }

        foreach (var s in _entry.Services)
        {
            var type = undo ? s.Original : s.Startup;
            sb.AppendLine($"  if (Get-Service -Name {Q(s.Name)} -ErrorAction SilentlyContinue) {{");
            // Windows PowerShell's Set-Service has no delayed start; sc.exe does.
            sb.AppendLine(type == "AutomaticDelayedStart"
                ? $"    sc.exe config {s.Name} start= delayed-auto | Out-Null"
                : $"    Set-Service -Name {Q(s.Name)} -StartupType {type} -ErrorAction Continue");
            sb.AppendLine("  }");
        }

        if (!undo)
            foreach (var f in _entry.Features)
                sb.AppendLine($"  Enable-WindowsOptionalFeature -Online -FeatureName {Q(f)} -All -NoRestart -ErrorAction Stop | Out-Null; Write-Host 'Enabled {f.Replace("'", "''")}'");

        var script = undo ? _entry.UndoScript : _entry.Script;
        if (!string.IsNullOrWhiteSpace(script))
        {
            sb.AppendLine("  & {");
            sb.AppendLine(script);
            sb.AppendLine("  }");
        }

        sb.AppendLine("} catch { Write-Host \"ERROR: $($_.Exception.Message)\"; $osxosFailed = $true }");
        sb.AppendLine("Stop-Transcript | Out-Null");
        sb.AppendLine("if ($osxosFailed) { exit 1 } else { exit 0 }");
        return sb.ToString();
    }

    public static ShellCommand PowerShellFile(string file) =>
        new("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file);

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        if (_entry.Kind == ScriptKind.Launcher)
        {
            var open = new ShellCommand("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", _entry.Script ?? "");
            var opened = await _runner.RunAsync(open, ct).ConfigureAwait(false);
            return opened.Ok
                ? ToolResult.Success($"Opened {_entry.Name}", _entry.Script ?? "")
                : ToolResult.Failure($"Could not open {_entry.Name}", opened.Message);
        }

        var undo = _entry.Kind == ScriptKind.Toggle && ToggleApplied;
        var basePath = _tempBase();
        var scriptFile = basePath + ".ps1";
        var logFile = basePath + ".log";

        try
        {
            // UTF-8 with a byte-order mark: Windows PowerShell reads anything else as ANSI.
            await File.WriteAllTextAsync(scriptFile, BuildScript(undo, logFile), new UTF8Encoding(true), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure("Could not prepare the script", ex.Message);
        }

        ProcessOutcome outcome;
        try
        {
            if (!_elevation.CanElevate)
                return ToolResult.Failure("Administrator rights are not available", "WinUtil's scripts run as administrator.");
            progress?.Report(new ToolProgress(0, 0, "Waiting for administrator approval..."));
            outcome = await _elevation.RunElevatedAsync(PowerShellFile(scriptFile), ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(scriptFile); } catch { }
        }

        if (outcome.ExitCode == 1223)
            return ToolResult.Failure("Cancelled", "Administrator rights were declined, so nothing was changed.");

        var log = ReadLog(logFile);
        try { File.Delete(logFile); } catch { }

        var lines = new List<string>();
        if (_entry.Kind == ScriptKind.Toggle)
        {
            var (set, total) = ToggleProgress();
            var wanted = undo ? 0 : total;
            lines.Add(undo
                ? $"{total - set} of {total} changes are back to their originals."
                : $"{set} of {total} changes are in place.");
            if (set != wanted && outcome.Ok) outcome = outcome with { ExitCode = 2 };
        }
        lines.AddRange(log);
        if (_entry.NeedsRestart || _entry.Kind == ScriptKind.Feature) lines.Add("Restart Windows to finish.");
        if (_entry.Kind == ScriptKind.Toggle) lines.Add("Run this tool again to switch back.");

        return outcome.Ok
            ? ToolResult.Success(undo ? $"Undone: {_entry.Name}" : $"Done: {_entry.Name}", lines.ToArray())
            : ToolResult.Failure($"{_entry.Name} did not finish cleanly", new[] { $"PowerShell exited with code {outcome.ExitCode}." }.Concat(lines).ToArray());
    }

    /// <summary>What the script printed, without the transcript's own header and footer.</summary>
    static IReadOnlyList<string> ReadLog(string path)
    {
        try
        {
            if (!File.Exists(path)) return Array.Empty<string>();
            var lines = File.ReadAllLines(path)
                .Where(l => l.Trim().Length > 0 && !l.StartsWith("*****", StringComparison.Ordinal))
                .Where(l => !l.Contains("Windows PowerShell transcript", StringComparison.Ordinal))
                .Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"^(Start time|End time|Username|RunAs User|Configuration Name|Machine|Host Application|Process ID|PSVersion|PSEdition|PSCompatibleVersions|BuildVersion|CLRVersion|WSManStackVersion|PSRemotingProtocolVersion|SerializationVersion|Transcript started|Windows PowerShell transcript end)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .ToList();
            return lines.Count > 20 ? lines.Skip(lines.Count - 20).Prepend($"… {lines.Count - 20} earlier lines").ToList() : lines;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
