namespace OsXos.Tools.Windows;

/// <summary>
/// Clears the installers Windows Update has downloaded. Routinely several gigabytes
/// on a machine that has been upgraded a few times, and the standard first move when
/// an update keeps failing to install.
///
/// The first tool in osXos that genuinely needs administrator rights: the folder is
/// owned by the system and the service holding it has to be stopped first. It is
/// therefore also the tool that exercises <see cref="IElevationService"/> — one
/// named command, elevated, rather than osXos running as administrator.
/// </summary>
public sealed class UpdateCacheTool : ITool
{
    /// <summary>Where Windows Update stages what it has downloaded.</summary>
    public static string DefaultDownloadDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");

    /// <summary>
    /// Stop the service, remove the folder, start it again — as one elevated command,
    /// so the user sees a single UAC prompt rather than three. cmd's &amp;&amp; chains
    /// them, and rd needs /s /q to take a populated tree without prompting.
    /// </summary>
    public static ShellCommand CommandFor(string downloadDir) => new(
        "cmd.exe", "/c",
        $"net stop wuauserv && rd /s /q \"{downloadDir}\" && net start wuauserv");

    readonly string _downloadDir;
    readonly IElevationService _elevation;

    public UpdateCacheTool(IElevationService elevation) : this(DefaultDownloadDir, elevation) { }

    public UpdateCacheTool(string downloadDir, IElevationService elevation)
    {
        _downloadDir = downloadDir;
        _elevation = elevation;
    }

    public string Id => "windows.update-cache";
    public OSKind Platform => OSKind.Windows;
    public ToolCategory Category => ToolCategory.Maintenance;
    public string Name => "Clear Windows Update Cache";
    public string Summary => "Delete the update installers Windows has already downloaded.";
    public string IconKey => "IconDownload";
    public string? Warning => "The Windows Update service stops and restarts. Do not run this while an update is installing — let it finish first, or it will have to download everything again.";
    public bool IsDestructive => true;

    /// <summary>The only tool here that cannot work inside the user's own account.</summary>
    public bool RequiresElevation => true;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Measure what has been downloaded",
            @"C:\Windows\SoftwareDistribution\Download holds every update package Windows has fetched, including ones already installed and ones that failed. It is readable without administrator rights, so the size on the Review stage is the real figure."),
        new("Stop the Windows Update service",
            "The folder is held open by wuauserv while it runs, so nothing can be deleted until the service stops. This is why the job needs administrator rights at all."),
        new("Delete the folder and start the service again",
            "All three steps run as one elevated command, so Windows asks for permission once rather than three times. The service is restarted whether or not every file could be removed."),
        new("Windows rebuilds it on demand",
            "The folder is a cache; Windows recreates it and re-downloads whatever it still needs. The first update check afterwards will take longer and use bandwidth. Nothing already installed is affected, and this does not uninstall any update."),
    };

    public Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_downloadDir))
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"Nothing to clear — {_downloadDir} does not exist. Windows has not downloaded any updates, or the cache has already been cleared."));
        }

        var size = FileSweep.SizeOf(_downloadDir, ct);
        if (size == 0)
        {
            return Task.FromResult(ToolPreview.Blocked(
                $"Nothing to clear — {_downloadDir} is empty."));
        }

        var items = new[]
        {
            new PreviewItem("Downloaded update packages", _downloadDir, size),
            new PreviewItem("Will run, elevated", CommandFor(_downloadDir).Display),
        };

        return Task.FromResult(new ToolPreview(items,
            $"{FileSweep.FormatBytes(size)} to reclaim · needs administrator rights")
        {
            NeedsElevation = true,
        });
    }

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        if (!_elevation.CanElevate)
        {
            return ToolResult.Failure("Administrator rights are not available",
                "osXos could not find a way to ask for them on this system.",
                "Run this in an elevated Command Prompt instead:",
                CommandFor(_downloadDir).Display);
        }

        var before = FileSweep.SizeOf(_downloadDir, ct);
        progress?.Report(new ToolProgress(0, 0, "Waiting for administrator approval..."));

        var command = CommandFor(_downloadDir);
        var outcome = await _elevation.RunElevatedAsync(command, ct).ConfigureAwait(false);

        // 1223 is ERROR_CANCELLED: the UAC prompt was dismissed. Declining is a
        // choice, not a fault, and should not be reported as a failure of the tool.
        if (outcome.ExitCode == 1223)
        {
            return ToolResult.Failure("Cancelled",
                "Administrator rights were declined, so nothing was changed.");
        }

        if (!outcome.Ok)
        {
            return ToolResult.Failure("Could not clear the update cache",
                command.Display,
                $"exit code {outcome.ExitCode}",
                string.IsNullOrWhiteSpace(outcome.Message)
                    ? "The Windows Update service may still be stopped — check Services if updates stop working."
                    : outcome.Message);
        }

        var after = Directory.Exists(_downloadDir) ? FileSweep.SizeOf(_downloadDir, ct) : 0;
        var freed = Math.Max(0, before - after);

        var lines = new List<string>
        {
            $"Reclaimed {FileSweep.FormatBytes(freed)}.",
            "The Windows Update service was stopped and started again.",
            "Windows will re-download whatever it still needs on the next update check.",
        };
        if (after > 0)
            lines.Add($"{FileSweep.FormatBytes(after)} remains — some files were in use.");

        return ToolResult.Success(
            $"Update cache cleared — {FileSweep.FormatBytes(freed)} reclaimed", lines.ToArray());
    }
}
