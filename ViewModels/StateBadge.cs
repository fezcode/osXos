using OsXos.Tools;
using ReactiveUI;

namespace OsXos.ViewModels;

/// <summary>
/// A tool's current state as the pill beside it shows it. Empty until read, and
/// stays empty for tools that have no state to report — cleanup tools report sizes
/// instead, on their Review stage and the All Tools page.
/// </summary>
public sealed class StateBadge : ViewModelBase
{
    ToolState? _state;
    public ToolState? State
    {
        get => _state;
        set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(HasState));
            this.RaisePropertyChanged(nameof(Label));
            this.RaisePropertyChanged(nameof(Detail));
            this.RaisePropertyChanged(nameof(IsOn));
            this.RaisePropertyChanged(nameof(IsPartial));
            this.RaisePropertyChanged(nameof(IsUnavailable));
        }
    }

    public bool HasState => State is not null;
    public string Label => State?.Label ?? "";
    public string? Detail => State?.Detail;
    public bool IsOn => State?.Tone == StateTone.On;
    public bool IsPartial => State?.Tone == StateTone.Partial;
    public bool IsUnavailable => State?.Tone == StateTone.Unavailable;

    /// <summary>Reads the state off the UI thread. A failed read shows nothing rather than a guess.</summary>
    public async Task LoadAsync(ITool tool, CancellationToken ct = default)
    {
        if (tool is not IHasState stateful) return;
        try
        {
            State = await Task.Run(() => stateful.ReadStateAsync(ct), ct).ConfigureAwait(true);
        }
        catch
        {
            State = null;
        }
    }
}
