using OsXos.Tools;
using OsXos.ViewModels;

namespace OsXos.Menus;

/// <summary>
/// osXos described as a set of menus, plus the one place a menu id is turned into an
/// action. Both the Hisashi bridge and the native macOS bridge read from here, so
/// there is exactly one menu and it cannot drift between platforms.
///
/// Every id <see cref="BuildTree"/> produces must be answered by <see cref="Invoke"/>;
/// a test walks the real tree and asserts that, because a dead menu row is the kind of
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

    public IReadOnlyList<AppMenuNode> Build() => BuildTree(
        _os,
        _tools,
        _vm.NavItems.Select(n => n.Name).ToList(),
        _vm.SelectedNav?.Name,
        _vm.HasQuery);

    /// <summary>
    /// The tree itself, built from plain data so it can be exercised without a window.
    /// The instance <see cref="Build"/> is only this plus the view model's state.
    /// </summary>
    public static IReadOnlyList<AppMenuNode> BuildTree(
        OSKind os,
        ToolRegistry tools,
        IReadOnlyList<string> pages,
        string? currentPage,
        bool hasQuery)
    {
        // macOS puts Quit in the application menu with Cmd+Q and has no Alt+F4; the
        // hint is only ever a hint, but a wrong one is worse than none.
        var quitKey = os == OSKind.MacOS ? "Cmd+Q" : "Alt+F4";
        var prefsKey = os == OSKind.MacOS ? "Cmd+," : "Ctrl+,";

        // On macOS the first submenu *becomes* the application menu, so it is named
        // after the app and owns About, Settings and Quit - that is the platform
        // convention. Everywhere else the host already shows the app's name beside
        // the menus (Hisashi draws it, and a Linux panel shows it too), so repeating
        // it gave a menubar reading "osXos osXos Tools View Help". Those platforms
        // get a plain File menu instead, and About lives only under Help where it
        // belongs.
        var appMenu = os == OSKind.MacOS
            ? AppMenuNode.Menu("app", "osXos",
                AppMenuNode.Item("app.about", "About osXos"),
                AppMenuNode.Separator(),
                AppMenuNode.Item("app.settings", "Settings", prefsKey),
                AppMenuNode.Item("app.data", "Open Settings Folder"),
                AppMenuNode.Separator(),
                AppMenuNode.Item("app.quit", "Quit osXos", quitKey))
            : AppMenuNode.Menu("app", "File",
                AppMenuNode.Item("app.settings", "Settings", prefsKey),
                AppMenuNode.Item("app.data", "Open Settings Folder"),
                AppMenuNode.Separator(),
                AppMenuNode.Item("app.quit", "Quit osXos", quitKey));

        return new List<AppMenuNode>
        {
            appMenu,

            // One submenu per populated category, each listing its tools. This is the
            // menu bar earning its place: every tool in the app is two clicks away
            // without touching the sidebar.
            new("tools", "Tools", Items: tools.Categories
                .Select(c => AppMenuNode.Menu($"cat.{c.Category}", c.Name,
                    tools.InCategory(c.Category)
                        .Select(t => AppMenuNode.Item($"tool.{t.Id}", t.Name + "..."))
                        .ToArray()))
                .ToList()),

            new("view", "View", Items: pages
                .Select(name => AppMenuNode.Item($"view.{name}", name, check: currentPage == name))
                .Concat(new[]
                {
                    AppMenuNode.Separator(),
                    AppMenuNode.Item("view.clearsearch", "Clear Search", enabled: hasQuery),
                })
                .ToList()),

            AppMenuNode.Menu("help", "Help",
                AppMenuNode.Item("help.project", "osXos on GitHub"),
                AppMenuNode.Item("help.about", "About osXos")),
        };
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
