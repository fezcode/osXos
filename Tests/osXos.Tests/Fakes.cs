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
