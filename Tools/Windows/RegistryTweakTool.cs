using System.Globalization;
using System.Text;

namespace OsXos.Tools.Windows;

public enum RegValueKind
{
    DWord,
    QWord,
    String,
}

/// <summary>
/// One registry value a tweak sets. <paramref name="Original"/> is what running the
/// tool again puts back; null means "remove the value", which for a policy is
/// Windows' own default.
/// </summary>
public sealed record TweakValue(
    RegHive Hive, string KeyPath, string Name, RegValueKind Kind, string Value, string? Original, string Label)
{
    public string HiveName => Hive switch
    {
        RegHive.CurrentUser => "HKEY_CURRENT_USER",
        RegHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        _ => "HKEY_USERS",
    };

    /// <summary>The value as the registry editor would name it.</summary>
    public string Display => $@"{HiveName}\{KeyPath}\{Name}";
}

/// <summary>
/// Everything that makes one registry tweak different from another. A tweak is
/// either a thing switched off ("Disable Activity History") or a preference with
/// two states ("Mouse Acceleration"); the engine is the same, only the words differ.
/// </summary>
public sealed record RegistryTweak(
    string Slug,
    ToolCategory Category,
    string Name,
    string Summary,
    string IconKey,
    IReadOnlyList<TweakValue> Values,
    bool RestartsExplorer,
    string AppliedText,
    string RevertedText,
    IReadOnlyList<ToolStep> Steps)
{
    /// <summary>True for a two-state preference, whose preview reads "on → off".</summary>
    public bool IsPreference { get; init; }

    /// <summary>Extra caution shown before it runs — WinUtil's "advanced" tweaks carry one.</summary>
    public string? Caution { get; init; }

    /// <summary>Some values are only read at boot; the result says so rather than implying it is live.</summary>
    public bool NeedsRestart { get; init; }

    /// <summary>Where the values came from, when osXos did not choose them itself.</summary>
    public string? Source { get; init; }

    /// <summary>State pill when every value is set — "Recall off", "Disabled". Defaults by kind.</summary>
    public string? AppliedLabel { get; init; }

    /// <summary>State pill when no value is set — "Recall on", "Enabled". Defaults by kind.</summary>
    public string? NotAppliedLabel { get; init; }

    /// <summary>
    /// Says why the thing this tweak controls is not on this machine at all — Recall
    /// on a PC that is not Copilot+, Brave when Brave is not installed — or null
    /// when it is. The tweak can still be applied (the values are harmless, and
    /// ready if the feature arrives), but its state says plainly that there is
    /// nothing here to switch off.
    /// </summary>
    public Func<IProcessRunner, CancellationToken, Task<string?>>? Unavailable { get; init; }

    /// <summary>Anything outside the current user's hive needs administrator rights to write.</summary>
    public bool MachineWide => Values.Any(v => v.Hive != RegHive.CurrentUser);
}

/// <summary>
/// A Windows setting changed the way Group Policy and WinUtil change it — a handful
/// of registry values — and changed back by restoring exactly the originals. A
/// two-way toggle: applied when every value already holds the tweak's value, and
/// running it again undoes it.
///
/// Writes go through <c>reg import</c> of a file osXos writes, which is precisely
/// what double-clicking a .reg file does. It handles every value kind and a removal
/// in one step, and it needs no quoting for keys with spaces in them — which matters
/// because the machine-wide tweaks run it elevated, and an elevated command line is
/// the last place to be hand-escaping quotes. Afterwards every value is read back,
/// so the result reports what the registry now holds rather than what was asked for.
/// </summary>
public sealed class RegistryTweakTool : ITool, IHasState
{
    readonly RegistryTweak _tweak;
    readonly IRegistryAccess _registry;
    readonly IUserRegistry _broadcast;
    readonly IShellController _shell;
    readonly IProcessRunner _runner;
    readonly IElevationService _elevation;
    readonly Func<string> _tempFile;

    public RegistryTweakTool(
        RegistryTweak tweak, IRegistryAccess registry, IUserRegistry broadcast, IShellController shell,
        IProcessRunner runner, IElevationService elevation, Func<string>? tempFile = null)
    {
        _tweak = tweak;
        _registry = registry;
        _broadcast = broadcast;
        _shell = shell;
        _runner = runner;
        _elevation = elevation;
        _tempFile = tempFile ?? (() => Path.Combine(Path.GetTempPath(), $"osxos-{tweak.Slug}-{Guid.NewGuid():N}.reg"));
    }

    public RegistryTweak Tweak => _tweak;

    public string Id => "windows." + _tweak.Slug;
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => _tweak.Category;
    public string Name => _tweak.Name;
    public string Summary => _tweak.Summary;
    public string IconKey => _tweak.IconKey;
    public bool IsDestructive => false;
    public bool RequiresElevation => _tweak.MachineWide;
    public IReadOnlyList<ToolStep> Steps => _tweak.Steps;

    public string? Warning
    {
        get
        {
            var parts = new List<string>();
            if (_tweak.Caution is { } caution) parts.Add(caution);
            if (_tweak.RestartsExplorer)
                parts.Add("Explorer restarts so the change takes effect, closing any open File Explorer windows.");
            if (_tweak.NeedsRestart) parts.Add("Windows only reads this at startup: it takes effect after a restart.");
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    // ---- state ----

    /// <summary>What a value holds now, in the same text form the tweak is written in; null when absent.</summary>
    string? Current(TweakValue v) => _registry.GetValue(v.Hive, v.KeyPath, v.Name) switch
    {
        null => null,
        int i => v.Kind == RegValueKind.DWord ? unchecked((uint)i).ToString(CultureInfo.InvariantCulture) : i.ToString(CultureInfo.InvariantCulture),
        long l => v.Kind == RegValueKind.QWord ? unchecked((ulong)l).ToString(CultureInfo.InvariantCulture) : l.ToString(CultureInfo.InvariantCulture),
        string s => s,
        var other => other.ToString(),
    };

    static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    bool Applied => _tweak.Values.All(v => Same(Current(v), v.Value));

    /// <summary>
    /// "Recall off", "Enabled", "Partly applied" — or "Not on this PC" when what the
    /// tweak controls does not exist here. Registry reads plus, for the few tweaks
    /// that check availability, one quick query.
    /// </summary>
    public async Task<ToolState> ReadStateAsync(CancellationToken ct)
    {
        var set = _tweak.Values.Count(v => Same(Current(v), v.Value));
        var all = set == _tweak.Values.Count;

        if (_tweak.Unavailable is { } check &&
            await check(_runner, ct).ConfigureAwait(false) is { } reason)
        {
            return new ToolState("Not on this PC", StateTone.Unavailable,
                all ? reason + " The values are set anyway, ready if it ever arrives." : reason);
        }

        var applied = _tweak.AppliedLabel ?? (_tweak.IsPreference ? "On" : "Applied");
        var notApplied = _tweak.NotAppliedLabel ?? (_tweak.IsPreference ? "Off" : "Not applied");

        return set switch
        {
            0 => new ToolState(notApplied, _tweak.IsPreference ? StateTone.Off : StateTone.On,
                "None of this tweak's values are set."),
            _ when all => new ToolState(applied, _tweak.IsPreference ? StateTone.On : StateTone.Off,
                "Every value this tweak sets is in place."),
            _ => new ToolState(_tweak.IsPreference ? "Partly on" : "Partly applied", StateTone.Partial,
                $"{set} of {_tweak.Values.Count} values are set; running the tool sets the rest."),
        };
    }

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        var state = await ReadStateAsync(ct).ConfigureAwait(false);
        var applied = Applied;
        var items = _tweak.Values.Select(v =>
        {
            var now = Current(v) ?? "not set";
            var target = applied ? v.Original : v.Value;
            var targetText = target ?? "removed (Windows default)";
            var detail = !applied && Same(Current(v), v.Value)
                ? $"{v.Name} = {now} — already set"
                : $"{v.Name} = {now} → {targetText}";
            return new PreviewItem(v.Label, detail);
        }).ToList();

        if (_tweak.RestartsExplorer) items.Add(new PreviewItem("Then", "Restart Explorer so it picks the change up"));

        string summary;
        if (_tweak.IsPreference)
            summary = applied ? $"Currently on. Will turn it off — {_tweak.RevertedText}" : $"Currently off. Will turn it on — {_tweak.AppliedText}";
        else
            summary = applied ? $"Already applied. Will undo it — {_tweak.RevertedText}" : $"Will apply it — {_tweak.AppliedText}";
        if (_tweak.MachineWide) summary += " · needs administrator rights";
        if (state.Tone == StateTone.Unavailable) summary = $"{state.Detail} {summary}";

        return new ToolPreview(items, summary) { NeedsElevation = _tweak.MachineWide, State = state };
    }

    // ---- run ----

    /// <summary>
    /// The .reg file for one direction: every value set to its target, or removed
    /// with <c>"Name"=-</c> where the target is "remove". Public for the tests.
    /// </summary>
    public static string RegFile(IEnumerable<(TweakValue Value, string? Target)> changes)
    {
        var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        foreach (var group in changes.GroupBy(c => (c.Value.HiveName, c.Value.KeyPath)))
        {
            sb.Append("\r\n[").Append(group.Key.HiveName).Append('\\').Append(group.Key.KeyPath).Append("]\r\n");
            foreach (var (value, target) in group)
            {
                sb.Append('"').Append(Escape(value.Name)).Append("\"=");
                sb.Append(target is null ? "-" : Data(value.Kind, target)).Append("\r\n");
            }
        }
        return sb.ToString();
    }

    static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    static string Data(RegValueKind kind, string value) => kind switch
    {
        RegValueKind.DWord => "dword:" + uint.Parse(value, CultureInfo.InvariantCulture).ToString("x8", CultureInfo.InvariantCulture),
        // .reg files spell a QWORD as eight little-endian bytes.
        RegValueKind.QWord => "hex(b):" + string.Join(",",
            BitConverter.GetBytes(ulong.Parse(value, CultureInfo.InvariantCulture)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))),
        _ => "\"" + Escape(value) + "\"",
    };

    public static ShellCommand ImportCommand(string file) => new("reg.exe", "import", file);

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var reverting = Applied;
        var changes = _tweak.Values.Select(v => (Value: v, Target: reverting ? v.Original : v.Value)).ToList();

        var file = _tempFile();
        try
        {
            // UTF-16 with a byte-order mark is the format reg import expects.
            await File.WriteAllTextAsync(file, RegFile(changes), Encoding.Unicode, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure("Could not prepare the change", ex.Message);
        }

        ProcessOutcome outcome;
        try
        {
            var import = ImportCommand(file);
            if (_tweak.MachineWide)
            {
                if (!_elevation.CanElevate)
                    return ToolResult.Failure("Administrator rights are not available",
                        "Some of these values are machine-wide and need them.");
                progress?.Report(new ToolProgress(0, 0, "Waiting for administrator approval..."));
                outcome = await _elevation.RunElevatedAsync(import, ct).ConfigureAwait(false);
            }
            else
            {
                outcome = await _runner.RunAsync(import, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try { File.Delete(file); } catch { /* a leftover .reg in temp is harmless */ }
        }

        if (outcome.ExitCode == 1223)
            return ToolResult.Failure("Cancelled", "Administrator rights were declined, so nothing was changed.");

        // Read back rather than trust the exit code: the elevated route reports
        // nothing but that code, and the registry is the thing that matters.
        var missed = changes.Where(c => !Same(Current(c.Value), c.Target)).ToList();
        if (missed.Count == changes.Count)
            return ToolResult.Failure("The setting did not change",
                new[] { $"reg import exited with code {outcome.ExitCode}." }
                    .Concat(missed.Select(m => $"{m.Value.Display} is still {Current(m.Value) ?? "not set"}")).ToArray());

        _broadcast.BroadcastSettingChange("Policy");

        var lines = new List<string>
        {
            reverting
                ? $"Restored {changes.Count - missed.Count} of {changes.Count} value{(changes.Count == 1 ? "" : "s")} to Windows' original."
                : $"Set {changes.Count - missed.Count} of {changes.Count} value{(changes.Count == 1 ? "" : "s")}.",
        };
        lines.AddRange(missed.Select(m => $"Could not change {m.Value.Display} — it is {Current(m.Value) ?? "not set"}."));

        if (_tweak.RestartsExplorer)
        {
            progress?.Report(new ToolProgress(0, 0, "Restarting Explorer..."));
            await _shell.StopExplorerAsync(ct).ConfigureAwait(false);
            lines.Add(await _shell.StartExplorerAsync(ct).ConfigureAwait(false)
                ? "Explorer was restarted."
                : "Explorer did not come back by itself — press Ctrl+Shift+Esc, choose Run new task, and enter explorer.");
        }

        if (_tweak.NeedsRestart) lines.Add("Restart Windows for this to take effect.");
        lines.Add("Run this tool again to switch back.");

        var headline = _tweak.IsPreference
            ? $"{_tweak.Name}: {(reverting ? "off" : "on")}"
            : reverting ? $"Undone: {_tweak.Name}" : $"Done: {_tweak.Name}";
        return ToolResult.Success(headline, lines.ToArray());
    }
}
