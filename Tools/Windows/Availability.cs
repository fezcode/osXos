namespace OsXos.Tools.Windows;

/// <summary>
/// Whether the thing a tweak controls exists on this PC at all. Each check returns
/// the reason it does not, or null when it does — and each is cheap and read-only:
/// a file test, or one query Windows answers without administrator rights.
/// </summary>
public static class Availability
{
    /// <summary>
    /// Recall ships as an optional Windows feature, present only on Copilot+ PCs.
    /// Win32_OptionalFeature lists it — installed or not — wherever it exists, and
    /// answers without administrator rights, unlike DISM.
    /// </summary>
    public static readonly ShellCommand RecallQuery = new("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
        "(Get-CimInstance -ClassName Win32_OptionalFeature -Filter 'Name=''Recall''').InstallState");

    public static async Task<string?> Recall(IProcessRunner runner, CancellationToken ct)
    {
        var outcome = await runner.RunAsync(RecallQuery, ct).ConfigureAwait(false);
        // A failed query says nothing either way; treat it as present rather than
        // claim a feature is missing on the strength of an error.
        if (!outcome.Ok) return null;
        return outcome.StdOut.Trim().Length == 0
            ? "Recall is not part of Windows on this PC — it only comes with Copilot+ PCs."
            : null;
    }

    public static Task<string?> Brave(IProcessRunner runner, CancellationToken ct) =>
        Task.FromResult(AnyExists(
            @"BraveSoftware\Brave-Browser\Application\brave.exe")
            ? null
            : "Brave is not installed on this PC.");

    public static Task<string?> Edge(IProcessRunner runner, CancellationToken ct) =>
        Task.FromResult(AnyExists(@"Microsoft\Edge\Application\msedge.exe")
            ? null
            : "Microsoft Edge is not installed on this PC.");

    /// <summary>Whether a program exists under either Program Files or the per-user Local AppData.</summary>
    static bool AnyExists(string relative)
    {
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        return roots.Any(r => r.Length > 0 && File.Exists(Path.Combine(r, relative)));
    }
}
