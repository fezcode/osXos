using OsXos.Tools;
using OsXos.Tools.Windows;

namespace OsXos.Tests;

/// <summary>
/// Records every command it is asked to run and answers from a script. This is how
/// the macOS and Linux tools are tested from a Windows machine: the argv they build
/// and the way they read a result are checkable here even though the commands
/// themselves can only run on their own platform.
/// </summary>
public sealed class FakeRunner : IProcessRunner
{
    readonly Dictionary<string, ProcessOutcome> _scripted = new(StringComparer.Ordinal);

    public List<ShellCommand> Ran { get; } = new();
    public HashSet<string> Available { get; } = new(StringComparer.Ordinal);

    /// <summary>When nothing is scripted for a command, it is treated as succeeding.</summary>
    public ProcessOutcome Default { get; set; } = new(0, "", "");

    public FakeRunner WithBinary(params string[] files)
    {
        foreach (var f in files) Available.Add(f);
        return this;
    }

    public FakeRunner Returns(string display, int exitCode, string stdout = "", string stderr = "")
    {
        _scripted[display] = new ProcessOutcome(exitCode, stdout, stderr);
        return this;
    }

    public Task<ProcessOutcome> RunAsync(ShellCommand command, CancellationToken ct)
    {
        Ran.Add(command);
        return Task.FromResult(_scripted.TryGetValue(command.Display, out var o) ? o : Default);
    }

    public bool Exists(string file) => Available.Contains(file);

    public IReadOnlyList<string> RanDisplays => Ran.Select(c => c.Display).ToList();
}

/// <summary>In-memory Explorer settings, so the toggle logic never touches the registry.</summary>
public sealed class FakeExplorerSettings : IExplorerAdvancedSettings
{
    public int Hidden { get; set; } = 2;
    public int HideFileExt { get; set; } = 1;
    public int NotifyCount { get; private set; }

    public void NotifyShell() => NotifyCount++;
}

/// <summary>Records what the icon cache tool asked of the shell, and restarts nothing.</summary>
public sealed class FakeShellController : IShellController
{
    public bool IsExplorerRunning { get; set; } = true;
    public int StopCount { get; private set; }
    public int StartCount { get; private set; }
    public int ProcessesToStop { get; set; } = 1;
    public bool StartSucceeds { get; set; } = true;

    public Task<int> StopExplorerAsync(CancellationToken ct)
    {
        StopCount++;
        return Task.FromResult(ProcessesToStop);
    }

    public Task<bool> StartExplorerAsync(CancellationToken ct)
    {
        StartCount++;
        return Task.FromResult(StartSucceeds);
    }
}

/// <summary>An in-memory registry, so the registry-backed tools never touch a real hive.</summary>
public sealed class FakeRegistry : IRegistryAccess
{
    readonly Dictionary<string, Dictionary<string, object>> _values = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deleted { get; } = new();

    static string K(RegHive hive, string keyPath) => $"{hive}::{keyPath}";

    public FakeRegistry Set(RegHive hive, string keyPath, string name, object value)
    {
        if (!_values.TryGetValue(K(hive, keyPath), out var bag))
            _values[K(hive, keyPath)] = bag = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        bag[name] = value;
        return this;
    }

    /// <summary>Creates a key with no values, which a subkey listing must still see.</summary>
    public FakeRegistry AddKey(RegHive hive, string keyPath)
    {
        if (!_values.ContainsKey(K(hive, keyPath)))
            _values[K(hive, keyPath)] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        return this;
    }

    public object? GetValue(RegHive hive, string keyPath, string name) =>
        _values.TryGetValue(K(hive, keyPath), out var bag) && bag.TryGetValue(name, out var v) ? v : null;

    public IReadOnlyList<string> ValueNames(RegHive hive, string keyPath) =>
        _values.TryGetValue(K(hive, keyPath), out var bag) ? bag.Keys.ToList() : Array.Empty<string>();

    public IReadOnlyList<string> SubKeyNames(RegHive hive, string keyPath)
    {
        var prefix = K(hive, keyPath) + "\\";
        return _values.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool DeleteValue(RegHive hive, string keyPath, string name)
    {
        if (!_values.TryGetValue(K(hive, keyPath), out var bag) || !bag.Remove(name)) return false;
        Deleted.Add($"{keyPath}\\{name}");
        return true;
    }

    public bool DeleteSubKeyTree(RegHive hive, string keyPath, string subKey)
    {
        var prefix = K(hive, keyPath) + "\\" + subKey;
        var hit = _values.Keys
            .Where(k => k.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (hit.Count == 0) return false;
        foreach (var k in hit) _values.Remove(k);
        Deleted.Add($"{keyPath}\\{subKey}");
        return true;
    }
}

/// <summary>A Recycle Bin that reports what it is told and empties only in memory.</summary>
public sealed class FakeRecycleBin : IRecycleBin
{
    public RecycleBinState State { get; set; } = new(0, 0);
    public int EmptyCount { get; private set; }
    public bool EmptySucceeds { get; set; } = true;

    /// <summary>What stays behind after an empty, for the locked-file case.</summary>
    public RecycleBinState Remaining { get; set; } = new(0, 0);

    public RecycleBinState Query() => State;

    public bool Empty()
    {
        EmptyCount++;
        if (!EmptySucceeds) return false;
        State = Remaining;
        return true;
    }
}

/// <summary>A temp directory that cleans itself up.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "osxos-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string File(string name, int bytes = 0)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, new byte[bytes]);
        return full;
    }

    public string Dir(string name)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(full);
        return full;
    }

    public string Sub(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* a test that left a handle open should not fail the run */ }
    }
}
