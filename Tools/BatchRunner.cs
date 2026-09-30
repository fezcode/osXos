namespace OsXos.Tools;

/// <summary>Where one tool in a batch has got to.</summary>
public enum BatchStatus
{
    Queued,
    Running,
    Done,
    Failed,
    Skipped,
}

/// <summary>One tool to run in a batch, with the preview the user was shown for it.</summary>
public sealed record BatchStep(ITool Tool, ToolPreview Preview);

/// <summary>
/// Runs several tools one after another — the All Tools page's Run selected. The
/// same contract as the tool window, applied in a row: each tool acts only on the
/// preview the user saw for it, never on a fresh scan they did not.
///
/// Sequential on purpose. Several tools restart Explorer or Finder, one asks for
/// administrator rights, and two sweeps can share a parent folder; running them at
/// once would make every one of those interactions a race.
/// </summary>
public static class BatchRunner
{
    /// <param name="report">Called on each status change, with the result once there is one.</param>
    /// <param name="stopRequested">Checked between tools; once true, the rest are skipped.</param>
    public static async Task<IReadOnlyList<(ITool Tool, BatchStatus Status, ToolResult? Result)>> RunAsync(
        IReadOnlyList<BatchStep> steps,
        Action<ITool, BatchStatus, ToolResult?> report,
        Func<bool>? stopRequested = null,
        Func<ITool, IProgress<ToolProgress>?>? progressFor = null,
        CancellationToken ct = default)
    {
        var outcomes = new List<(ITool, BatchStatus, ToolResult?)>();
        foreach (var step in steps) report(step.Tool, BatchStatus.Queued, null);

        foreach (var step in steps)
        {
            var tool = step.Tool;

            if (stopRequested?.Invoke() == true || ct.IsCancellationRequested)
            {
                Finish(tool, BatchStatus.Skipped, ToolResult.Failure("Not run", "Stopped before this tool's turn."));
                continue;
            }

            // Defensive: the page never queues these, but a report has nothing to
            // run and a blocked preview has nothing to act on.
            if (tool.IsReadOnly || !step.Preview.CanRun)
            {
                Finish(tool, BatchStatus.Skipped, ToolResult.Failure("Not run",
                    tool.IsReadOnly ? "A read-only report has nothing to run." : step.Preview.Blocker ?? "Nothing to do."));
                continue;
            }

            report(tool, BatchStatus.Running, null);

            ToolResult result;
            try
            {
                var progress = progressFor?.Invoke(tool);
                result = await Task.Run(() => tool.RunAsync(step.Preview, ct, progress), ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                result = ToolResult.Failure("Cancelled", "The run was cancelled.");
            }
            catch (Exception ex)
            {
                result = ToolResult.Failure("Failed", ex.Message);
            }

            Finish(tool, result.Ok ? BatchStatus.Done : BatchStatus.Failed, result);
        }

        return outcomes;

        void Finish(ITool tool, BatchStatus status, ToolResult result)
        {
            outcomes.Add((tool, status, result));
            report(tool, status, result);
        }
    }

    /// <summary>
    /// For each tool another selected tool already covers, the name of the one that
    /// covers it. A tool covering itself, or one only covered by something not
    /// selected, is not in the map.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CoveredBy(IEnumerable<ITool> selected)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tool in selected)
            foreach (var id in tool.Covers)
                if (id != tool.Id) map.TryAdd(id, tool.Name);
        return map;
    }
}
