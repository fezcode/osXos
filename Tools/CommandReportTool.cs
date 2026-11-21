namespace OsXos.Tools;

/// <summary>Local command diagnostics, captured once during Review and never rerun by Result.</summary>
public abstract class CommandReportTool : ITool
{
    readonly IProcessRunner _runner;
    protected abstract ShellCommand Command { get; }

    protected CommandReportTool(IProcessRunner runner) => _runner = runner;

    public abstract string Id { get; }
    public abstract OSKind Platform { get; }
    protected virtual string MissingCommandHint => "Install the system utility or check that it is on PATH.";
    public abstract ToolCategory Category { get; }
    public abstract string Name { get; }
    public abstract string Summary { get; }
    public abstract string IconKey { get; }
    public string? Warning => null;
    public bool IsDestructive => false;
    public bool IsReadOnly => true;
    public abstract IReadOnlyList<ToolStep> Steps { get; }

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_runner.Exists(Command.File))
            return ToolPreview.Blocked($"{Path.GetFileName(Command.File)} could not be found. {MissingCommandHint}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        ProcessOutcome outcome;
        try
        {
            outcome = await _runner.RunAsync(Command, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return ToolPreview.Blocked("The diagnostic did not finish within 20 seconds. Try inspecting again.");
        }

        // ProcessRunner can return cancellation as a failed outcome instead of throwing.
        ct.ThrowIfCancellationRequested();
        if (timeout.IsCancellationRequested)
            return ToolPreview.Blocked("The diagnostic did not finish within 20 seconds. Try inspecting again.");

        if (!outcome.Ok)
        {
            var details = string.Join("\n", new[] { outcome.StdOut.Trim(), outcome.StdErr.Trim() }
                .Where(s => s.Length > 0));
            return ToolPreview.Blocked($"Could not read {Name.ToLowerInvariant()} (exit code {outcome.ExitCode}).\n{details}".Trim());
        }

        if (string.IsNullOrWhiteSpace(outcome.StdOut))
            return ToolPreview.Blocked("The diagnostic returned no report. Try inspecting again.");

        // Keep the utility's localized wording, headings and indentation. One line per row
        // fits ToolWindow's fixed-height virtualized list, even for large driver inventories.
        var items = outcome.StdOut.Replace("\r\n", "\n").Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new PreviewItem(line.TrimEnd()))
            .ToList();
        if (!string.IsNullOrWhiteSpace(outcome.StdErr))
            items.AddRange(outcome.StdErr.Replace("\r\n", "\n").Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => new PreviewItem(line.TrimEnd(), "Diagnostic message")));

        return new ToolPreview(items, "Read-only snapshot. Nothing was changed. Inspect again to refresh.");
    }

    public Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!preview.CanRun)
            return Task.FromResult(ToolResult.Failure("Report unavailable", preview.Blocker!));

        return Task.FromResult(ToolResult.Success(Name,
            preview.Items.Select(i => i.Detail is null ? i.Label : $"{i.Label} — {i.Detail}").ToArray()));
    }
}
