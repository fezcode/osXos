using System.Reactive;
using Avalonia.Media;
using OsXos.Tools;
using ReactiveUI;

namespace OsXos.ViewModels;

/// <summary>One tool, as a card in a category list.</summary>
public sealed class ToolCardViewModel : ViewModelBase
{
    public ToolCardViewModel(ITool tool, Func<ITool, Task> open)
    {
        Tool = tool;
        Icon = Icons.Get(tool.IconKey);
        OpenCommand = ReactiveCommand.CreateFromTask(() => open(tool));
    }

    public ITool Tool { get; }
    public StreamGeometry Icon { get; }
    public string Name => Tool.Name;
    public string Summary => Tool.Summary;
    public bool IsDestructive => Tool.IsDestructive;
    public int StepCount => Tool.Steps.Count;
    public string StepCountText => $"{Tool.Steps.Count} steps";
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
}

/// <summary>
/// A category page: its tools as cards. Also used for search results, where the
/// heading names the query instead of a category.
/// </summary>
public sealed class CategoryViewModel : ViewModelBase
{
    public CategoryViewModel(
        CategoryInfo info, OSKind os, IReadOnlyList<ITool> tools, Func<ITool, Task> open)
    {
        Info = info;
        Name = info.Name;
        Icon = Icons.Get(info.IconKey);
        OsName = CategoryCatalog.DisplayName(os);
        Cards = tools.Select(t => new ToolCardViewModel(t, open)).ToList();
        CountText = Cards.Count == 1 ? "1 tool" : $"{Cards.Count} tools";

        // The badge states what is true of this category rather than repeating a
        // slogan: it stops being honest the moment one tool here needs elevation.
        var elevated = tools.Count(t => t.RequiresElevation);
        NeedsElevation = elevated > 0;
        ElevationText = elevated == 0
            ? "No elevation needed"
            : elevated == tools.Count
                ? "Needs administrator"
                : $"{elevated} of {tools.Count} need administrator";
    }

    public CategoryInfo Info { get; }
    public string Name { get; }
    public StreamGeometry Icon { get; }
    public string OsName { get; }
    public IReadOnlyList<ToolCardViewModel> Cards { get; }
    public string CountText { get; }
    public bool NeedsElevation { get; }
    public string ElevationText { get; }
}

/// <summary>
/// The page shown while the top-bar search box has text in it. Deliberately a
/// separate view model rather than a mode on <see cref="CategoryViewModel"/>: it has
/// its own empty state, and results span categories.
/// </summary>
public sealed class SearchViewModel : ViewModelBase
{
    readonly ToolRegistry _registry;
    readonly Func<ITool, Task> _open;

    public SearchViewModel(ToolRegistry registry, Func<ITool, Task> open)
    {
        _registry = registry;
        _open = open;
    }

    string _query = "";
    public string Query
    {
        get => _query;
        private set => this.RaiseAndSetIfChanged(ref _query, value);
    }

    List<ToolCardViewModel> _cards = new();
    public List<ToolCardViewModel> Cards
    {
        get => _cards;
        private set
        {
            this.RaiseAndSetIfChanged(ref _cards, value);
            this.RaisePropertyChanged(nameof(HasResults));
            this.RaisePropertyChanged(nameof(CountText));
        }
    }

    public bool HasResults => Cards.Count > 0;

    public string CountText => Cards.Count switch
    {
        0 => "No tool matches",
        1 => "1 tool matches",
        _ => $"{Cards.Count} tools match",
    };

    public void Apply(string query)
    {
        Query = query;
        Cards = _registry.Search(query).Select(t => new ToolCardViewModel(t, _open)).ToList();
    }
}
