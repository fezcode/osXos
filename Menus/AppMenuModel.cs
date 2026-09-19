using OsXos.Tools;
using OsXos.ViewModels;

namespace OsXos.Menus;

/// <summary>
/// osXos described as a set of menus, plus the one place a menu id is turned into an
/// action. Both the Hisashi bridge and the native macOS bridge read from here, so
/// there is exactly one menu and it cannot drift between platforms.
///
/// Every id <see cref="Build"/> produces must be answered by <see cref="Invoke"/>;
/// a test walks the tree and asserts that, because a dead menu row is the kind of
/// thing nobody notices until a user clicks it.
/// </summary>
public sealed class AppMenuModel
{
    readonly MainWindowViewModel _vm;
    readonly ToolRegistry _tools;
    readonly OSKind _os;

    public AppMenuModel(MainWindowViewModel vm, ToolRegistry tools, OSKind os)
    {
        _vm = vm;
        _tools = tools;
        _os = os;
    }

    /// <summary>Raised whenever a command changes something the menu displays.</summary>
    public event Action? Changed;

    public IReadOnlyList<AppMenuNode> Build()
    {
        var page = _vm.SelectedNav?.Name;

        // macOS puts Quit in the application menu with Cmd+Q and has no Alt+F4; the
        // hint is only ever a hint, but a wrong one is worse than none.
        var quitKey = _os == OSKind.MacOS ? "Cmd+Q" : "Alt+F4";
        var prefsKey = _os == OSKind.MacOS ? "Cmd+," : "Ctrl+,";

        var menus = new List<AppMenuNode>
        {
            AppMenuNode.Menu("app", "osXos",
                AppMenuNode.Item("app.about", "About osXos"),
                AppMenuNode.Separator(),
                AppMenuNode.Item("app.settings", "Settings", prefsKey),
                AppMenuNode.Item("app.data", "Open Settings Folder"),
                AppMenuNode.Separator(),
                AppMenuNode.Item("app.quit", "Quit osXos", quitKey)),

            // One submenu per populated category, each listing its tools. This is the
            // menu bar earning its place: every tool in the app is two clicks away
            // without touching the sidebar.
            new("tools", "Tools", Items: _tools.Categories
                .Select(c => AppMenuNode.Menu($"cat.{c.Category}", c.Name,
                    _tools.InCategory(c.Category)
                        .Select(t => AppMenuNode.Item($"tool.{t.Id}", t.Name + "..."))
                        .ToArray()))
                .ToList()),

            new("view", "View", Items: _vm.NavItems
                .Select(n => AppMenuNode.Item($"view.{n.Name}", n.Name, check: page == n.Name))
                .Concat(new[]
                {
                    AppMenuNode.Separator(),
                    AppMenuNode.Item("view.clearsearch", "Clear Search", enabled: _vm.HasQuery),
                })
                .ToList()),

            AppMenuNode.Menu("help", "Help",
                AppMenuNode.Item("help.project", "osXos on GitHub"),
                AppMenuNode.Item("help.about", "About osXos")),
        };

        return menus;
    }

    /// <summary>
    /// Runs one menu id. Unknown ids are ignored rather than thrown on: the menu in
    /// Hisashi's bar may be a version behind what this build knows about.
    /// </summary>
    public void Invoke(string id)
    {
        if (id.StartsWith("view.", StringComparison.Ordinal) &&
            _vm.NavItems.FirstOrDefault(n => n.Name == id[5..]) is { } nav)
        {
            _vm.SelectedNav = nav;
            Changed?.Invoke();
            return;
        }

        if (id.StartsWith("tool.", StringComparison.Ordinal) &&
            _tools.Tools.FirstOrDefault(t => t.Id == id[5..]) is { } tool)
        {
            _ = _vm.OpenToolAsync(tool);
            return;
        }

        switch (id)
        {
            case "app.about": _vm.OpenAboutCommand.Execute().Subscribe(); break;
            case "app.settings": _vm.GoToSettings(); break;
            case "app.data": _vm.Settings.OpenDataFolderCommand.Execute().Subscribe(); break;
            case "app.quit": Shutdown(); break;

            case "view.clearsearch": _vm.Query = ""; break;

            case "help.project": _vm.OpenProjectCommand.Execute().Subscribe(); break;
            case "help.about": _vm.OpenAboutCommand.Execute().Subscribe(); break;
        }

        Changed?.Invoke();
    }

    /// <summary>Ids this model answers that are not derived from a list at build time.</summary>
    public static readonly IReadOnlyList<string> StaticIds = new[]
    {
        "app.about", "app.settings", "app.data", "app.quit",
        "view.clearsearch", "help.project", "help.about",
    };

    static void Shutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
