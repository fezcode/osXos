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

    public ToolWindowViewModel(ITool tool, OSKind os, bool alwaysExplain)
    {
        _tool = tool;

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

    public bool HasBlocker => !string.IsNullOrEmpty(Blocker);
    public bool CanRun => !HasBlocker && !Inspecting && !Running;

    async Task InspectAsync()
    {
        try
        {
            var preview = await Task.Run(() => _tool.InspectAsync(_cts.Token), _cts.Token).ConfigureAwait(true);
            _preview = preview;
            Rows = preview.Items.Select(ToRow).ToList();
            PreviewSummary = preview.Summary;
            Blocker = preview.Blocker;
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
        ToolResult result;
        try
        {
            result = await Task.Run(() => _tool.RunAsync(_preview, _cts.Token), _cts.Token).ConfigureAwait(true);
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

    public void Cancel() => _cts.Cancel();
}
