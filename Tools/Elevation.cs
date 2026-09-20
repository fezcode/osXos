using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace OsXos.Tools;

/// <summary>
/// Running something with administrator rights, and knowing whether we already have
/// them.
///
/// osXos's standing rule is that a tool works inside the user's own account. This
/// exists for the tools that genuinely cannot — a machine-wide cache, a system
/// service — so that when one arrives it has somewhere to plug in rather than each
/// tool inventing its own prompt. Nothing ships using it yet.
///
/// The deliberate shape: osXos never runs *itself* elevated. It asks the OS to run
/// one named command elevated, which keeps the window, the settings and every other
/// tool at normal rights, and means a user can see exactly what was escalated.
/// </summary>
public interface IElevationService
{
    /// <summary>Whether this process is already running with administrator rights.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Whether elevation can even be attempted here. False on a Linux box with no
    /// pkexec, where the honest answer is to show the command and let the user run it.
    /// </summary>
    bool CanElevate { get; }

    /// <summary>
    /// How the OS will ask. Shown in the UI before anything happens, so the prompt
    /// that appears is never a surprise.
    /// </summary>
    string PromptDescription { get; }

    /// <summary>
    /// Runs one command with administrator rights, prompting the user. Returns the
    /// same outcome shape as an ordinary run; a refused prompt is a non-zero exit,
    /// not an exception.
    /// </summary>
    Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct);
}

public sealed class ElevationService : IElevationService
{
    readonly IProcessRunner _runner;

    public ElevationService(IProcessRunner runner) => _runner = runner;

    public bool IsElevated
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows()) return WindowsIsElevated();
                // On macOS and Linux an effective uid of 0 is the whole question.
                return Environment.GetEnvironmentVariable("USER") == "root"
                       || Environment.GetEnvironmentVariable("EUID") == "0";
            }
            catch
            {
                return false;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    static bool WindowsIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool CanElevate => OperatingSystem.IsWindows()
                              || OperatingSystem.IsMacOS()
                              || _runner.Exists("pkexec");

    public string PromptDescription
    {
        get
        {
            if (IsElevated) return "osXos is already running with administrator rights.";
            if (OperatingSystem.IsWindows())
                return "Windows will show a User Account Control prompt naming the command before it runs.";
            if (OperatingSystem.IsMacOS())
                return "macOS will ask for your password before the command runs.";
            return CanElevate
                ? "polkit will ask for your password before the command runs."
                : "No way to ask for administrator rights was found on this system. Run the command yourself in a terminal.";
        }
    }

    public Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct)
    {
        if (IsElevated) return _runner.RunAsync(command, ct);

        if (OperatingSystem.IsWindows()) return RunAsWindowsAdminAsync(command, ct);
        if (OperatingSystem.IsMacOS()) return _runner.RunAsync(AppleScriptFor(command), ct);
        if (_runner.Exists("pkexec")) return _runner.RunAsync(PkexecFor(command), ct);

        return Task.FromResult(new ProcessOutcome(
            -1, "", "No elevation mechanism is available on this system."));
    }

    /// <summary>
    /// macOS asks through AppleScript, which is the only route that shows the native
    /// authorisation dialog rather than a terminal password prompt nothing can type
    /// into. The inner command is quoted for the shell AppleScript hands it to.
    /// </summary>
    public static ShellCommand AppleScriptFor(ShellCommand command) => new(
        "osascript", "-e",
        $"do shell script \"{EscapeForAppleScript(command.Display)}\" with administrator privileges");

    /// <summary>
    /// The string lands inside an AppleScript double-quoted literal, which is then
    /// handed to a shell: backslashes and double quotes have to survive both.
    /// </summary>
    public static string EscapeForAppleScript(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>polkit keeps the argument vector intact, so nothing needs escaping.</summary>
    public static ShellCommand PkexecFor(ShellCommand command) =>
        new("pkexec", new[] { command.File }.Concat(command.Args).ToList());

    /// <summary>
    /// Windows elevates by re-launching through the shell with the "runas" verb, which
    /// is what raises UAC. It needs UseShellExecute, and that rules out capturing
    /// stdout - so the outcome carries the exit code and nothing else, and a tool
    /// using this has to verify its work by looking at the system afterwards rather
    /// than by reading output.
    /// </summary>
    [SupportedOSPlatform("windows")]
    static async Task<ProcessOutcome> RunAsWindowsAdminAsync(ShellCommand command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(command.File)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in command.Args) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
                return new ProcessOutcome(-1, "", $"Could not start {command.File} elevated.");

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return new ProcessOutcome(proc.ExitCode, "", proc.ExitCode == 0 ? "" : "The elevated command failed.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user dismissed the UAC prompt. Not a fault.
            return new ProcessOutcome(1223, "", "Administrator rights were declined.");
        }
        catch (Exception ex)
        {
            return new ProcessOutcome(-1, "", ex.Message);
        }
    }
}
