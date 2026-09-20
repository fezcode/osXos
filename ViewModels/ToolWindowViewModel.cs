using System.Reactive;
using Avalonia.Media;
using OsXos.Tools;
using ReactiveUI;

namespace OsXos.ViewModels;

public enum ToolStage
{
    Explain,
    Review,
    Result,
}

/// <summary>One numbered row on the Explain stage.</summary>
public sealed class StepRow
{
    public required int Number { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public string NumberText => Number.ToString("0");
}

/// <summary>One found item on the Review stage.</summary>
public sealed class PreviewRow
{
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public string SizeText { get; init; } = "";
    public bool HasSize => SizeText.Length > 0;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>
/// Drives the one window every tool opens in. Three stages in a fixed order: Explain
/// says what will happen, Review says what was found, Result says what did happen.
///
/// The inspection starts as soon as the window opens and runs while the steps are
/// being read, so Continue is usually instant. It is strictly read-only — nothing a
/// tool can do to the machine happens before Run is pressed on a preview the user has
/// been shown.
/// </summary>
public sealed class ToolWindowViewModel : ViewModelBase
{
    readonly ITool _tool;
    readonly CancellationTokenSource _cts = new();

    ToolPreview? _preview;

    readonly IElevationService? _elevation;

    public ToolWindowViewModel(
        ITool tool, OSKind os, bool alwaysExplain, IElevationService? elevation = null)
    {
        _tool = tool;
        _elevation = elevation;

        Title = tool.Name;
        Eyebrow = $"{CategoryCatalog.Find(os, tool.Category)?.Name ?? tool.Category.ToString()} · {CategoryCatalog.DisplayName(os)}";
        Summary = tool.Summary;
        Icon = Icons.Get(tool.IconKey);
        Warning = tool.Warning;
        HasWarning = !string.IsNullOrWhiteSpace(tool.Warning);
        IsDestructive = tool.IsDestructive;
        RunLabel = tool.IsDestructive ? "Run" : "Apply";

        Steps = tool.Steps
            .Select((s, i) => new StepRow { Number = i + 1, Title = s.Title, Detail = s.Detail })
            .ToList();

        // A tool that always needs administrator rights says so before the scan; a
        // run that turns out to need them says so after (see ToolPreview).
        NeedsElevation = tool.RequiresElevation;

        _stage = alwaysExplain ? ToolStage.Explain : ToolStage.Review;

        ContinueCommand = ReactiveCommand.Create(() => { Stage = ToolStage.Review; });
        BackCommand = ReactiveCommand.Create(() => { Stage = ToolStage.Explain; });
        CloseCommand = ReactiveCommand.Create(() => { CloseRequested?.Invoke(); });
        RunCommand = ReactiveCommand.CreateFromTask(RunAsync);

        _ = InspectAsync();
    }

    /// <summary>Raised when the window should close itself.</summary>
    public event Action? CloseRequested;

    public string Title { get; }
    public string Eyebrow { get; }
    public string Summary { get; }
    public StreamGeometry Icon { get; }
    public string? Warning { get; }
    public bool HasWarning { get; }
    public bool IsDestructive { get; }
    public string RunLabel { get; }
    public IReadOnlyList<StepRow> Steps { get; }

    public ReactiveCommand<Unit, Unit> ContinueCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> RunCommand { get; }

    // ---- stage ----

    ToolStage _stage;
    public ToolStage Stage
    {
        get => _stage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _stage, value);
            this.RaisePropertyChanged(nameof(IsExplain));
            this.RaisePropertyChanged(nameof(IsReview));
            this.RaisePropertyChanged(nameof(IsResult));
            this.RaisePropertyChanged(nameof(ExplainDotActive));
            this.RaisePropertyChanged(nameof(ReviewDotActive));
            this.RaisePropertyChanged(nameof(ResultDotActive));
        }
    }

    public bool IsExplain => Stage == ToolStage.Explain;
    public bool IsReview => Stage == ToolStage.Review;
    public bool IsResult => Stage == ToolStage.Result;

    // The rail fills as you advance rather than marking only where you are, so the
    // three dots read as progress instead of as a radio group.
    public bool ExplainDotActive => true;
    public bool ReviewDotActive => Stage != ToolStage.Explain;
    public bool ResultDotActive => Stage == ToolStage.Result;

    // ---- review ----

    bool _inspecting = true;
    public bool Inspecting
    {
        get => _inspecting;
        private set => this.RaiseAndSetIfChanged(ref _inspecting, value);
    }

    List<PreviewRow> _rows = new();
    public List<PreviewRow> Rows
    {
        get => _rows;
        private set => this.RaiseAndSetIfChanged(ref _rows, value);
    }

    string _previewSummary = "";
    public string PreviewSummary
    {
        get => _previewSummary;
        private set => this.RaiseAndSetIfChanged(ref _previewSummary, value);
    }

    string? _blocker;
    /// <summary>
    /// Why the tool cannot run here. Shown in place of the found list, and the only
    /// thing standing between an honest "systemd-resolved is not installed" and a
    /// button that would have done nothing.
    /// </summary>
    public string? Blocker
    {
        get => _blocker;
        private set
        {
            this.RaiseAndSetIfChanged(ref _blocker, value);
            this.RaisePropertyChanged(nameof(HasBlocker));
            this.RaisePropertyChanged(nameof(CanRun));
        }
    }

    bool _needsElevation;
    /// <summary>
    /// Whether this run needs administrator rights. Shown as its own notice on
    /// Review, above the Run button, so the UAC or password prompt that follows is
    /// never a surprise.
    /// </summary>
    public bool NeedsElevation
    {
        get => _needsElevation;
        private set
        {
            this.RaiseAndSetIfChanged(ref _needsElevation, value);
            this.RaisePropertyChanged(nameof(ShowElevationNotice));
            this.RaisePropertyChanged(nameof(ElevationNotice));
        }
    }

    /// <summary>Hidden once we already have the rights: there is nothing to warn about.</summary>
    public bool ShowElevationNotice => NeedsElevation && _elevation?.IsElevated != true;

    public string ElevationNotice => _elevation is { } e
        ? e.PromptDescription
        : "This tool needs administrator rights.";

    public bool HasBlocker => !string.IsNullOrEmpty(Blocker);
    public bool CanRun => !HasBlocker && !Inspecting && !Running;

    async Task InspectAsync()
    {
        try
        {
            // Both the scan and the row projection run off the UI thread. A temp
            // folder with 40,000 entries makes the projection itself measurable, and
            // doing it on the dispatcher is the difference between a window that
            // says "Inspecting..." and one Windows marks Not Responding.
            var (preview, rows) = await Task.Run(async () =>
            {
                var p = await _tool.InspectAsync(_cts.Token).ConfigureAwait(false);
                return (p, p.Items.Select(ToRow).ToList());
            }, _cts.Token).ConfigureAwait(true);

            _preview = preview;
            Rows = rows;
            PreviewSummary = preview.Summary;
            Blocker = preview.Blocker;
            NeedsElevation = _tool.RequiresElevation || preview.NeedsElevation;
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-inspection.
        }
        catch (Exception ex)
        {
            Blocker = $"Could not inspect: {ex.Message}";
        }
        finally
        {
            Inspecting = false;
            this.RaisePropertyChanged(nameof(CanRun));
        }
    }

    static PreviewRow ToRow(PreviewItem item) => new()
    {
        Label = item.Label,
        Detail = item.Detail,
        SizeText = item.Bytes is { } b ? FileSweep.FormatBytes(b) : "",
    };

    // ---- run ----

    bool _running;
    public bool Running
    {
        get => _running;
        private set
        {
            this.RaiseAndSetIfChanged(ref _running, value);
            this.RaisePropertyChanged(nameof(CanRun));
        }
    }

    double _progressValue;
    /// <summary>0-100 for the bar. Meaningless while <see cref="ProgressIndeterminate"/>.</summary>
    public double ProgressValue
    {
        get => _progressValue;
        private set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    bool _progressIndeterminate = true;
    /// <summary>
    /// True until a tool reports countable work. A tool that runs one shell command
    /// never reports, and a bar that sat at 0% would read as stuck.
    /// </summary>
    public bool ProgressIndeterminate
    {
        get => _progressIndeterminate;
        private set => this.RaiseAndSetIfChanged(ref _progressIndeterminate, value);
    }

    string _progressText = "";
    public string ProgressText
    {
        get => _progressText;
        private set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    string _resultHeadline = "";
    public string ResultHeadline
    {
        get => _resultHeadline;
        private set => this.RaiseAndSetIfChanged(ref _resultHeadline, value);
    }

    List<string> _resultLines = new();
    public List<string> ResultLines
    {
        get => _resultLines;
        private set => this.RaiseAndSetIfChanged(ref _resultLines, value);
    }

    bool _resultOk;
    public bool ResultOk
    {
        get => _resultOk;
        private set
        {
            this.RaiseAndSetIfChanged(ref _resultOk, value);
            this.RaisePropertyChanged(nameof(ResultFailed));
        }
    }

    public bool ResultFailed => !ResultOk;

    async Task RunAsync()
    {
        if (_preview is null || !_preview.CanRun || Running) return;

        Running = true;
        ProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressText = "Starting...";

        // Progress<T> captures the current SynchronizationContext, so every report
        // is marshalled back to the UI thread for us.
        var progress = new Progress<ToolProgress>(OnProgress);

        ToolResult result;
        try
        {
            result = await Task.Run(
                () => _tool.RunAsync(_preview, _cts.Token, progress), _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = ToolResult.Failure("The tool did not finish", ex.Message);
        }
        finally
        {
            Running = false;
        }

        ResultOk = result.Ok;
        ResultHeadline = result.Headline;
        ResultLines = result.Lines.ToList();
        Stage = ToolStage.Result;
    }

    void OnProgress(ToolProgress p)
    {
        if (p.IsCountable)
        {
            ProgressIndeterminate = false;
            ProgressValue = p.Fraction * 100;
            var of = $"{p.Done:N0} of {p.Total:N0}";
            ProgressText = string.IsNullOrEmpty(p.Item) ? of : $"{of} — {p.Item}";
        }
        else
        {
            ProgressIndeterminate = true;
            ProgressText = p.Item ?? "Working...";
        }
    }

    public void Cancel() => _cts.Cancel();
}
