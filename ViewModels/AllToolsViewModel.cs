using System.Reactive;
using Avalonia.Media;
using OsXos.Tools;
using ReactiveUI;

namespace OsXos.ViewModels;

/// <summary>
/// One tool as a row on the All Tools page: a checkbox, what the tool would do on
/// this machine right now, and — once a batch has run — what it did.
/// </summary>
public sealed class BatchItemViewModel : ViewModelBase
{
    readonly AllToolsViewModel _owner;

    public BatchItemViewModel(ITool tool, AllToolsViewModel owner)
    {
        Tool = tool;
        _owner = owner;
        Icon = Icons.Get(tool.IconKey);
    }

    public ITool Tool { get; }
    public StreamGeometry Icon { get; }
    public string Name => Tool.Name;

    /// <summary>The setting's current state, from the scan, for tools that have one.</summary>
    public StateBadge State { get; } = new();
    public bool IsReadOnly => Tool.IsReadOnly;
    public bool IsDestructive => Tool.IsDestructive;

    // ---- current state ----

    ToolPreview? _preview;
    /// <summary>The last scan. What a batch acts on, exactly as the Review stage would.</summary>
    public ToolPreview? Preview
    {
        get => _preview;
        private set
        {
            this.RaiseAndSetIfChanged(ref _preview, value);
            RaiseState();
        }
    }

    bool _inspecting;
    public bool Inspecting
    {
        get => _inspecting;
        private set
        {
            this.RaiseAndSetIfChanged(ref _inspecting, value);
            RaiseState();
        }
    }

    string? _error;

    internal void BeginInspect()
    {
        _error = null;
        Inspecting = true;
    }

    internal void EndInspect(ToolPreview? preview, string? error)
    {
        _error = error;
        State.State = preview?.State;
        Preview = preview;
        Inspecting = false;
        if (!IsSelectable && IsChecked) IsChecked = false;
    }

    public bool NeedsAdmin => Tool.RequiresElevation || Preview?.NeedsElevation == true;

    /// <summary>What the tool would do now, in the tool's own words.</summary>
    public string StateText =>
        Inspecting ? "Checking…" :
        _error is not null ? $"Could not check: {_error}" :
        Preview is null ? "" :
        Preview.CanRun ? Preview.Summary :
        Preview.Blocker ?? "Nothing to do.";

    /// <summary>Dimmed when there is nothing to act on.</summary>
    public bool HasNothingToDo => !Inspecting && (Preview is null || !Preview.CanRun);

    public string SizeText => Preview is { CanRun: true, TotalBytes: > 0 } p ? FileSweep.FormatBytes(p.TotalBytes) : "";
    public bool HasSize => SizeText.Length > 0;

    // ---- selection ----

    string? _coveredBy;
    /// <summary>Set when another selected tool's run already includes this one.</summary>
    public string? CoveredBy
    {
        get => _coveredBy;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _coveredBy, value);
            this.RaisePropertyChanged(nameof(IsCovered));
            this.RaisePropertyChanged(nameof(CoveredText));
            this.RaisePropertyChanged(nameof(IsSelectable));
        }
    }

    public bool IsCovered => CoveredBy is not null;
    public string CoveredText => CoveredBy is { } by ? $"Included in {by}" : "";

    public bool IsSelectable =>
        !IsReadOnly && !Inspecting && Preview is { CanRun: true } && !IsCovered && !_owner.Running;

    bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (value && !IsSelectable) value = false;
            if (_isChecked == value) return;
            this.RaiseAndSetIfChanged(ref _isChecked, value);
            _owner.SelectionChanged();
        }
    }

    // ---- batch outcome ----

    BatchStatus? _status;
    public BatchStatus? Status
    {
        get => _status;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(HasStatus));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(IsRunningNow));
            this.RaisePropertyChanged(nameof(Succeeded));
            this.RaisePropertyChanged(nameof(Failed));
        }
    }

    public bool HasStatus => Status is not null;
    public bool IsRunningNow => Status == BatchStatus.Running;
    public bool Succeeded => Status == BatchStatus.Done;
    public bool Failed => Status is BatchStatus.Failed or BatchStatus.Skipped;

    public string StatusText => Status switch
    {
        BatchStatus.Queued => "Queued",
        BatchStatus.Running => "Running…",
        BatchStatus.Done => "Done",
        BatchStatus.Failed => "Failed",
        BatchStatus.Skipped => "Skipped",
        _ => "",
    };

    string _resultText = "";
    /// <summary>The tool's own result headline, shown under its state once it has run.</summary>
    public string ResultText
    {
        get => _resultText;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _resultText, value);
            this.RaisePropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => ResultText.Length > 0;

    internal void RaiseState()
    {
        this.RaisePropertyChanged(nameof(StateText));
        this.RaisePropertyChanged(nameof(HasNothingToDo));
        this.RaisePropertyChanged(nameof(SizeText));
        this.RaisePropertyChanged(nameof(HasSize));
        this.RaisePropertyChanged(nameof(NeedsAdmin));
        this.RaisePropertyChanged(nameof(IsSelectable));
    }
}

/// <summary>One tool's entry in the run report: what it was, how it went, what it said.</summary>
public sealed class BatchReportRow
{
    public required string Name { get; init; }
    public required StreamGeometry Icon { get; init; }
    public required BatchStatus Status { get; init; }
    public required string Headline { get; init; }
    public required IReadOnlyList<string> Lines { get; init; }

    public bool Succeeded => Status == BatchStatus.Done;
    public bool Failed => Status is BatchStatus.Failed or BatchStatus.Skipped;
    public bool HasLines => Lines.Count > 0;

    public string StatusText => Status switch
    {
        BatchStatus.Done => "Done",
        BatchStatus.Failed => "Failed",
        BatchStatus.Skipped => "Skipped",
        _ => Status.ToString(),
    };
}

/// <summary>One category's rows on the All Tools page.</summary>
public sealed class BatchGroupViewModel
{
    public required string Name { get; init; }
    public required StreamGeometry Icon { get; init; }
    public required IReadOnlyList<BatchItemViewModel> Items { get; init; }
}

/// <summary>
/// Every tool on one page, each with its current state and a checkbox, and one
/// button that runs everything ticked — the Chris Titus WinUtil shape, on osXos's
/// terms: nothing runs that the page has not shown you, and every tool acts on the
/// same preview its own Review stage would have.
///
/// Scans start the first time the page is opened, not when osXos launches: several
/// of them measure tens of gigabytes, and most sessions never visit this page.
/// </summary>
public sealed class AllToolsViewModel : ViewModelBase
{
    const int ParallelScans = 4;

    readonly OSKind _os;
    bool _started;
    CancellationTokenSource _cts = new();

    public AllToolsViewModel(ToolRegistry registry, OSKind os)
    {
        _os = os;
        OsName = CategoryCatalog.DisplayName(os);

        Groups = registry.Categories
            .Select(c => new BatchGroupViewModel
            {
                Name = c.Name,
                Icon = Icons.Get(c.IconKey),
                Items = registry.InCategory(c.Category).Select(t => new BatchItemViewModel(t, this)).ToList(),
            })
            .ToList();
        Items = Groups.SelectMany(g => g.Items).ToList();
        CountText = $"{Items.Count} tools · {Groups.Count} categories";

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        SelectAllCommand = ReactiveCommand.Create(SelectAll);
        ClearCommand = ReactiveCommand.Create(Clear);
        ReviewCommand = ReactiveCommand.Create(() => { if (CanRunSelected) ShowConfirm = true; });
        CancelConfirmCommand = ReactiveCommand.Create(() => { ShowConfirm = false; });
        ConfirmCommand = ReactiveCommand.CreateFromTask(RunSelectedAsync);
        StopCommand = ReactiveCommand.Create(() => { StopRequested = true; });
        CloseReportCommand = ReactiveCommand.Create(() => { ShowReport = false; });
        OpenReportCommand = ReactiveCommand.Create(() => { if (Report.Count > 0) ShowReport = true; });
    }

    public string OsName { get; }
    public string CountText { get; }
    public IReadOnlyList<BatchGroupViewModel> Groups { get; }
    public IReadOnlyList<BatchItemViewModel> Items { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }
    public ReactiveCommand<Unit, Unit> ReviewCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseReportCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenReportCommand { get; }

    // ---- scanning ----

    bool _scanning;
    public bool Scanning
    {
        get => _scanning;
        private set => this.RaiseAndSetIfChanged(ref _scanning, value);
    }

    /// <summary>Called when the page is shown. Scans once; Refresh scans again.</summary>
    public void EnsureInspected()
    {
        if (_started) return;
        _ = RefreshAsync();
    }

    public Task RefreshAsync() => InspectAsync(Items);

    async Task InspectAsync(IReadOnlyList<BatchItemViewModel> items)
    {
        if (Running) return;
        _started = true;
        Scanning = true;

        using var gate = new SemaphoreSlim(ParallelScans);
        var ct = _cts.Token;

        foreach (var item in items) item.BeginInspect();

        await Task.WhenAll(items.Select(async item =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(true);
            try
            {
                // Off the UI thread: several inspections walk large folders
                // synchronously before their first await.
                var preview = await Task.Run(async () =>
                {
                    var p = await item.Tool.InspectAsync(ct).ConfigureAwait(false);
                    // A toggle whose preview does not carry its state still has one to show.
                    if (p.State is null && item.Tool is IHasState stateful)
                        p = p with { State = await stateful.ReadStateAsync(ct).ConfigureAwait(false) };
                    return p;
                }, ct).ConfigureAwait(true);
                item.EndInspect(preview, null);
            }
            catch (OperationCanceledException)
            {
                item.EndInspect(null, "cancelled");
            }
            catch (Exception ex)
            {
                item.EndInspect(null, ex.Message);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(true);

        Scanning = false;
        SelectionChanged();
    }

    // ---- selection ----

    public IReadOnlyList<BatchItemViewModel> Selected => Items.Where(i => i.IsChecked).ToList();

    public int SelectedCount => Items.Count(i => i.IsChecked);

    public string SelectionText
    {
        get
        {
            var n = SelectedCount;
            if (n == 0) return "Nothing selected";
            var bytes = Items.Where(i => i.IsChecked).Sum(i => i.Preview?.TotalBytes ?? 0);
            var what = $"{n} tool{(n == 1 ? "" : "s")} selected";
            return bytes > 0 ? $"{what} · {FileSweep.FormatBytes(bytes)} to reclaim" : what;
        }
    }

    public bool CanRunSelected => SelectedCount > 0 && !Running && !Scanning;

    bool _applyingCovers;

    /// <summary>
    /// Keeps the covering rule true after any checkbox changes: a tool whose run is
    /// included in a selected one is unticked and disabled, with a note saying which.
    /// </summary>
    internal void SelectionChanged()
    {
        if (!_applyingCovers)
        {
            _applyingCovers = true;
            try
            {
                var covered = BatchRunner.CoveredBy(Items.Where(i => i.IsChecked).Select(i => i.Tool));
                foreach (var item in Items)
                {
                    item.CoveredBy = covered.TryGetValue(item.Tool.Id, out var by) ? by : null;
                    if (item.IsCovered && item.IsChecked) item.IsChecked = false;
                }
            }
            finally
            {
                _applyingCovers = false;
            }
        }

        this.RaisePropertyChanged(nameof(SelectedCount));
        this.RaisePropertyChanged(nameof(SelectionText));
        this.RaisePropertyChanged(nameof(CanRunSelected));
    }

    void SelectAll()
    {
        // In catalogue order, so a covering tool is ticked before — and so unticks —
        // the narrower ones it includes.
        foreach (var item in Items)
            if (item.IsSelectable) item.IsChecked = true;
    }

    void Clear()
    {
        foreach (var item in Items) item.IsChecked = false;
    }

    // ---- confirm ----

    bool _showConfirm;
    public bool ShowConfirm
    {
        get => _showConfirm;
        private set
        {
            this.RaiseAndSetIfChanged(ref _showConfirm, value);
            if (value)
            {
                this.RaisePropertyChanged(nameof(ConfirmTitle));
                this.RaisePropertyChanged(nameof(ConfirmNames));
                this.RaisePropertyChanged(nameof(ConfirmNotes));
            }
        }
    }

    public string ConfirmTitle => $"Run {SelectedCount} tool{(SelectedCount == 1 ? "" : "s")}?";

    public IReadOnlyList<string> ConfirmNames => Selected.Select(i => i.Name).ToList();

    /// <summary>The things worth knowing before pressing Run, and only those that apply.</summary>
    public IReadOnlyList<string> ConfirmNotes
    {
        get
        {
            var selected = Selected;
            var notes = new List<string>();

            var destructive = selected.Count(i => i.IsDestructive);
            if (destructive > 0)
                notes.Add($"{destructive} of them delete{(destructive == 1 ? "s" : "")} files permanently — nothing goes to the Recycle Bin.");

            var admin = selected.Where(i => i.NeedsAdmin).Select(i => i.Name).ToList();
            if (admin.Count > 0)
                notes.Add($"Administrator rights will be asked for, once per tool that needs them: {string.Join(", ", admin)}.");

            notes.Add("They run one at a time, in the order listed, each on exactly what this page showed for it.");
            return notes;
        }
    }

    // ---- run ----

    bool _running;
    public bool Running
    {
        get => _running;
        private set
        {
            this.RaiseAndSetIfChanged(ref _running, value);
            this.RaisePropertyChanged(nameof(CanRunSelected));
            foreach (var item in Items) item.RaiseState();
        }
    }

    bool _stopRequested;
    public bool StopRequested
    {
        get => _stopRequested;
        private set => this.RaiseAndSetIfChanged(ref _stopRequested, value);
    }

    string _runSummary = "";
    public string RunSummary
    {
        get => _runSummary;
        private set
        {
            this.RaiseAndSetIfChanged(ref _runSummary, value);
            this.RaisePropertyChanged(nameof(HasRunSummary));
        }
    }

    public bool HasRunSummary => RunSummary.Length > 0;

    // ---- report ----

    IReadOnlyList<BatchReportRow> _report = Array.Empty<BatchReportRow>();
    /// <summary>The last batch, tool by tool, in the order it ran. Kept until the next batch.</summary>
    public IReadOnlyList<BatchReportRow> Report
    {
        get => _report;
        private set => this.RaiseAndSetIfChanged(ref _report, value);
    }

    bool _showReport;
    public bool ShowReport
    {
        get => _showReport;
        private set => this.RaiseAndSetIfChanged(ref _showReport, value);
    }

    string _reportTitle = "";
    public string ReportTitle
    {
        get => _reportTitle;
        private set => this.RaiseAndSetIfChanged(ref _reportTitle, value);
    }

    /// <summary>Runs every ticked tool in page order. Public so tests can drive it without the dialog.</summary>
    public async Task RunSelectedAsync()
    {
        ShowConfirm = false;
        var selected = Selected;
        if (selected.Count == 0 || Running) return;

        foreach (var item in Items)
        {
            item.Status = null;
            item.ResultText = "";
        }

        StopRequested = false;
        RunSummary = "";
        Running = true;

        var byTool = selected.ToDictionary(i => i.Tool);
        var steps = selected.Select(i => new BatchStep(i.Tool, i.Preview!)).ToList();

        var outcomes = await BatchRunner.RunAsync(steps, (tool, status, result) =>
        {
            var item = byTool[tool];
            item.Status = status;
            if (result is not null) item.ResultText = result.Headline;
        }, () => StopRequested, ct: _cts.Token).ConfigureAwait(true);

        Running = false;

        foreach (var item in selected) item.IsChecked = false;

        var done = outcomes.Count(o => o.Status == BatchStatus.Done);
        var failed = outcomes.Count(o => o.Status == BatchStatus.Failed);
        var skipped = outcomes.Count(o => o.Status == BatchStatus.Skipped);
        RunSummary = string.Join(" · ", new[]
        {
            $"{done} done",
            failed > 0 ? $"{failed} failed" : null,
            skipped > 0 ? $"{skipped} skipped" : null,
        }.Where(s => s is not null));

        // The report opens by itself: a batch can take minutes, and what each tool
        // said at the end is the thing worth reading when it finishes.
        Report = outcomes.Select(o => new BatchReportRow
        {
            Name = o.Tool.Name,
            Icon = byTool[o.Tool].Icon,
            Status = o.Status,
            Headline = o.Result?.Headline ?? "",
            Lines = o.Result?.Lines ?? Array.Empty<string>(),
        }).ToList();
        ReportTitle = $"Ran {outcomes.Count} tool{(outcomes.Count == 1 ? "" : "s")} — {RunSummary}";
        ShowReport = true;

        // What ran has changed the machine; show its state as it is now.
        await InspectAsync(selected).ConfigureAwait(true);
    }
}
