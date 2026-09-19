using System.Reactive;
using Avalonia.Media;
using OsXos.Services;
using OsXos.Tools;
using ReactiveUI;

namespace OsXos.ViewModels;

/// <summary>One row in the sidebar navigation.</summary>
public sealed class NavItem
{
    public required StreamGeometry Icon { get; init; }
    public required string Name { get; init; }
    public required ViewModelBase Vm { get; init; }

    /// <summary>Tool count, shown as the pill on the right of the row.</summary>
    public string CountText { get; init; } = "";
    public bool HasCount => CountText.Length > 0;
}

/// <summary>
/// One stripe in the sidebar's OS badge strip — the osXos answer to Cogas's platform
/// spectrum. Three stripes, the running one lit, the other two dimmed but still
/// named on hover along with how many tools they carry.
/// </summary>
public sealed class OsBadgeViewModel
{
    public OsBadgeViewModel(OSKind os, bool isCurrent)
    {
        Os = os;
        IsCurrent = isCurrent;
        Name = CategoryCatalog.DisplayName(os);
        Icon = Icons.Get(CategoryCatalog.IconKey(os));
        Brush = Icons.Brush(CategoryCatalog.BrushKey(os));

        var count = ToolCatalog.CountFor(os);
        CountText = $"{count} tool{(count == 1 ? "" : "s")}";
        StateText = isCurrent
            ? "This is the build you are running."
            : $"Shipped in the {Name} build of osXos.";
    }

    public OSKind Os { get; }
    public bool IsCurrent { get; }
    public string Name { get; }
    public StreamGeometry Icon { get; }
    public IBrush Brush { get; }
    public string CountText { get; }
    public string StateText { get; }

    /// <summary>Lit for the running OS, faded for the other two.</summary>
    public double StripeOpacity => IsCurrent ? 1.0 : 0.28;
}

public sealed class MainWindowViewModel : ViewModelBase
{
    readonly AppServices _services;

    public SettingsViewModel Settings { get; }
    public SearchViewModel Search { get; }
    public List<NavItem> NavItems { get; }
    public List<OsBadgeViewModel> OsBadges { get; }

    /// <summary>What the user is asked to open a tool with — set by the view.</summary>
    public Func<ITool, ToolWindowViewModel, Task>? ShowToolWindow { get; set; }

    public MainWindowViewModel(AppServices services)
    {
        _services = services;

        Settings = new SettingsViewModel(services);
        Search = new SearchViewModel(services.Tools, OpenToolAsync);

        OsName = CategoryCatalog.DisplayName(services.OS);
        OsIcon = Icons.Get(CategoryCatalog.IconKey(services.OS));

        // Only categories that actually have a tool on this OS. An empty category is
        // never drawn, so there are no placeholder pages anywhere in osXos.
        NavItems = services.Tools.Categories
            .Select(c =>
            {
                var tools = services.Tools.InCategory(c.Category);
                return new NavItem
                {
                    Icon = Icons.Get(c.IconKey),
                    Name = c.Name,
                    CountText = tools.Count.ToString("0"),
                    Vm = new CategoryViewModel(c, services.OS, tools, OpenToolAsync),
                };
            })
            .ToList();

        NavItems.Add(new NavItem
        {
            Icon = Icons.Get("IconSettings"),
            Name = "Settings",
            Vm = Settings,
        });

        OsBadges = Enum.GetValues<OSKind>()
            .Select(os => new OsBadgeViewModel(os, os == services.OS))
            .ToList();

        _selectedNav = NavItems[0];
        _current = NavItems[0].Vm;

        var version = typeof(MainWindowViewModel).Assembly.GetName().Version;
        VersionText = $"v{version?.ToString(3) ?? "dev"}";
        AboutVersionText = $"Version {version?.ToString(3) ?? "dev"}";
        AboutRuntimeText = $".NET {Environment.Version.ToString(3)} · Avalonia UI";
        AboutPlatformText = $"{OsName} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";

        ConfirmSaveAndLeaveCommand = ReactiveCommand.Create(() =>
        {
            Settings.Save();
            ShowUnsavedPrompt = false;
            CommitPendingNav();
        });

        ConfirmDiscardAndLeaveCommand = ReactiveCommand.Create(() =>
        {
            Settings.Revert();
            ShowUnsavedPrompt = false;
            CommitPendingNav();
        });

        CancelUnsavedPromptCommand = ReactiveCommand.Create(() =>
        {
            PendingNav = null;
            ShowUnsavedPrompt = false;
            // The ListBox has already moved its highlight to the row that was
            // rejected, so it has to be told to put it back.
            this.RaisePropertyChanged(nameof(SelectedNav));
        });

        OpenAboutCommand = ReactiveCommand.Create(() => { ShowAbout = true; });
        CloseAboutCommand = ReactiveCommand.Create(() => { ShowAbout = false; });
        OpenProjectCommand = ReactiveCommand.Create(() =>
            OpenUrl("https://github.com/fezcode/osXos"));
        ClearSearchCommand = ReactiveCommand.Create(() => { Query = ""; });
    }

    public string OsName { get; }
    public StreamGeometry OsIcon { get; }
    public string VersionText { get; }
    public string AboutVersionText { get; }
    public string AboutRuntimeText { get; }
    public string AboutPlatformText { get; }

    // ---- navigation ----

    NavItem _selectedNav;
    public NavItem SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (_selectedNav == value || value == null) return;

            // Unsaved settings guard, matching Cogas: the move is held until the
            // prompt is answered rather than being silently dropped.
            if (Current == Settings && Settings.IsDirty && value.Vm != Settings)
            {
                PendingNav = value;
                ShowUnsavedPrompt = true;
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedNav, value);
            Navigate(value.Vm);
        }
    }

    void CommitPendingNav()
    {
        if (PendingNav is not { } target) return;
        PendingNav = null;
        this.RaiseAndSetIfChanged(ref _selectedNav, target, nameof(SelectedNav));
        Navigate(target.Vm);
    }

    void Navigate(ViewModelBase vm)
    {
        Query = "";
        Current = vm;
    }

    ViewModelBase _current;
    public ViewModelBase Current
    {
        get => _current;
        private set => this.RaiseAndSetIfChanged(ref _current, value);
    }

    /// <summary>The breadcrumb tail in the top bar.</summary>
    public string CrumbText => Current == Search ? $"Search “{Query}”" : SelectedNav?.Name ?? "Maintenance";

    // ---- search ----

    string _query = "";
    public string Query
    {
        get => _query;
        set
        {
            if (_query == value) return;
            this.RaiseAndSetIfChanged(ref _query, value);
            this.RaisePropertyChanged(nameof(HasQuery));

            if (string.IsNullOrWhiteSpace(value))
            {
                if (Current == Search) Current = SelectedNav?.Vm ?? NavItems[0].Vm;
            }
            else
            {
                Search.Apply(value);
                Current = Search;
            }
            this.RaisePropertyChanged(nameof(CrumbText));
        }
    }

    public bool HasQuery => Query.Length > 0;
    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }

    // ---- unsaved changes guard ----

    bool _showUnsavedPrompt;
    public bool ShowUnsavedPrompt
    {
        get => _showUnsavedPrompt;
        private set => this.RaiseAndSetIfChanged(ref _showUnsavedPrompt, value);
    }

    NavItem? _pendingNav;
    public NavItem? PendingNav
    {
        get => _pendingNav;
        private set => this.RaiseAndSetIfChanged(ref _pendingNav, value);
    }

    public ReactiveCommand<Unit, Unit> ConfirmSaveAndLeaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmDiscardAndLeaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelUnsavedPromptCommand { get; }

    // ---- about ----

    bool _showAbout;
    public bool ShowAbout
    {
        get => _showAbout;
        private set => this.RaiseAndSetIfChanged(ref _showAbout, value);
    }

    public ReactiveCommand<Unit, Unit> OpenAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseAboutCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenProjectCommand { get; }

    // ---- opening a tool ----

    async Task OpenToolAsync(ITool tool)
    {
        if (ShowToolWindow is not { } show) return;
        var vm = new ToolWindowViewModel(tool, _services.OS, _services.Settings.AlwaysExplain);
        await show(tool, vm);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            // No browser, or a sandbox that refuses. Not worth surfacing.
        }
    }

    /// <summary>Sidebar width, remembered across runs.</summary>
    public double SidebarWidth
    {
        get => _services.Settings.SidebarWidth ?? DefaultSidebarWidth;
        set => _services.Settings.Update(d => d with { SidebarWidth = Math.Clamp(value, MinSidebarWidth, MaxSidebarWidth) });
    }

    public const double DefaultSidebarWidth = 240;
    public const double MinSidebarWidth = 200;
    public const double MaxSidebarWidth = 380;
}
