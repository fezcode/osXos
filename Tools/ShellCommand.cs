using System.Diagnostics;
using System.Text;

namespace OsXos.Tools;

/// <summary>
/// One process invocation, held as file plus argument list rather than a command
/// string. Keeping the arguments apart is what makes the shell-driven tools testable:
/// a test asserts the exact argv without a process ever starting, which is the only
/// part of the macOS and Linux tools that can be verified on a Windows machine.
/// </summary>
public sealed record ShellCommand(string File, IReadOnlyList<string> Args)
{
    public ShellCommand(string file, params string[] args) : this(file, (IReadOnlyList<string>)args) { }

    /// <summary>The command as a human would type it — shown on Explain and Review.</summary>
    public string Display
    {
        get
        {
            if (Args.Count == 0) return File;
            var sb = new StringBuilder(File);
            foreach (var a in Args)
            {
                sb.Append(' ');
                sb.Append(a.Length == 0 || a.Any(char.IsWhiteSpace) ? "\"" + a + "\"" : a);
            }
            return sb.ToString();
        }
    }
}

public sealed record ProcessOutcome(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    /// <summary>stdout if it said anything, else stderr — whichever is worth showing.</summary>
    public string Message =>
        !string.IsNullOrWhiteSpace(StdOut) ? StdOut.Trim() : StdErr.Trim();
}

public interface IProcessRunner
{
    Task<ProcessOutcome> RunAsync(ShellCommand command, CancellationToken ct);

    /// <summary>Whether the executable can be found at all, for preview blockers.</summary>
    bool Exists(string file);
}

/// <summary>Runs commands for real. Never opens a window and never uses a shell.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(ShellCommand command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(command.File)
        {
            // UseShellExecute=false plus CreateNoWindow is what keeps a console from
            // flashing over the UI when a tool shells out.
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in command.Args) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return new ProcessOutcome(-1, "", $"could not start {command.File}");

            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return new ProcessOutcome(proc.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new ProcessOutcome(-1, "", ex.Message);
        }
    }

    public bool Exists(string file)
    {
        if (System.IO.File.Exists(file)) return true;
        if (System.IO.Path.IsPathRooted(file)) return false;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : new[] { "" };

        foreach (var dir in path.Split(System.IO.Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
            {
                try
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, file + ext))) return true;
                }
                catch
                {
                    // An unreadable or malformed PATH entry is not worth failing over.
                }
            }
        }
        return false;
    }
}
