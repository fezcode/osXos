namespace OsXos.Tools.Windows;

/// <summary>
/// The tweaks osXos chose itself, as opposed to those taken from WinUtil. Every value
/// is a per-user policy or setting Windows documents; none reaches
/// HKEY_LOCAL_MACHINE, so none needs administrator rights.
///
/// Deliberately absent: Widgets, whose per-user value Windows now guards against
/// third-party writes.
/// </summary>
public static class OsXosTweaks
{
    const string Policies = @"Software\Policies\Microsoft\Windows";
    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    static TweakValue User(string key, string name, int value, string label) =>
        new(RegHive.CurrentUser, key, name, RegValueKind.DWord, value.ToString(), null, label);

    public static readonly RegistryTweak BingSearch = new(
        Slug: "bing-search",
        Category: ToolCategory.Privacy,
        Name: "Turn Off Bing in Start Search",
        Summary: "Stop Start menu search sending what you type to Bing and mixing web results into your files and apps.",
        IconKey: "IconSearch",
        Values: new[]
        {
            User(Policies + @"\Explorer", "DisableSearchBoxSuggestions", 1, "Web results and suggestions in Start search"),
            User(Search, "BingSearchEnabled", 0, "Bing search from the Start menu"),
            User(Search, "CortanaConsent", 0, "Cortana web search consent"),
        },
        RestartsExplorer: true,
        AppliedText: "Start search looks only at this PC.",
        RevertedText: "Start search shows Bing results again.",
        Steps: new ToolStep[]
        {
            new("What Start search does by default",
                "Everything typed into the Start menu is also sent to Bing, and web results and trending searches are mixed in with your own apps, files and settings."),
            new("Set the per-user policy",
                @"DisableSearchBoxSuggestions under HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Explorer — the Group Policy setting Microsoft documents for this — plus BingSearchEnabled and CortanaConsent under the Search key, which older builds read instead."),
            new("Restart Explorer",
                "Start search reads these when it starts, so Explorer is restarted once. Searching this PC, apps and settings works exactly as before."),
            new("Run it again to undo",
                "The values are removed rather than flipped, which is Windows' own default — as if osXos had never set them. No administrator rights; only your account is affected."),
        })
    {
        AppliedLabel = "Bing off",
        NotAppliedLabel = "Bing on",
    };

    public static readonly RegistryTweak Copilot = new(
        Slug: "copilot-off",
        Category: ToolCategory.AI,
        Name: "Turn Off Copilot",
        Summary: "Switch off Windows Copilot, its taskbar button, and the Copilot sidebar in Edge.",
        IconKey: "IconAI",
        Values: new[]
        {
            User(Policies + @"\WindowsCopilot", "TurnOffWindowsCopilot", 1, "Windows Copilot"),
            User(Advanced, "ShowCopilotButton", 0, "Copilot button on the taskbar"),
            User(@"Software\Policies\Microsoft\Edge", "HubsSidebarEnabled", 0, "Copilot sidebar in Microsoft Edge"),
        },
        RestartsExplorer: true,
        AppliedText: "Copilot is off in Windows and Edge.",
        RevertedText: "Copilot and its button come back.",
        Steps: new ToolStep[]
        {
            new("Turn off Windows Copilot",
                @"TurnOffWindowsCopilot under HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsCopilot, the per-user Group Policy setting, and ShowCopilotButton = 0 so the taskbar button goes too."),
            new("Turn off Edge's sidebar",
                "HubsSidebarEnabled = 0 in Edge's per-user policies, which removes the sidebar the Copilot button lives in. Edge shows \"managed by your organization\" in its settings while a policy is set — that is the policy speaking, not an employer."),
            new("What it does not do",
                "The newer Copilot app installed from the Store is an ordinary app: this keeps it out of the taskbar and Windows, but uninstalling it is done from Settings → Apps like any other."),
            new("Restart Explorer, and undo by running again",
                "Explorer restarts once so the taskbar redraws. Running the tool again removes every value it set. No administrator rights."),
        })
    {
        AppliedLabel = "Copilot off",
        NotAppliedLabel = "Copilot on",
    };

    public static readonly RegistryTweak Recall = new(
        Slug: "recall-off",
        Category: ToolCategory.AI,
        Name: "Turn Off Recall & Click to Do",
        Summary: "Stop Recall saving snapshots of your screen, and switch off Click to Do — on Copilot+ PCs that have them.",
        IconKey: "IconEye",
        Values: new[]
        {
            User(Policies + @"\WindowsAI", "DisableAIDataAnalysis", 1, "Recall screen snapshots"),
            User(Policies + @"\WindowsAI", "DisableClickToDo", 1, "Click to Do"),
        },
        RestartsExplorer: false,
        AppliedText: "Recall saves no snapshots and Click to Do is off.",
        RevertedText: "Recall and Click to Do follow their own settings again.",
        Steps: new ToolStep[]
        {
            new("Only matters on Copilot+ PCs",
                "Recall and Click to Do exist only on Copilot+ PCs. Elsewhere these values are harmless and change nothing, so the tool is safe to run anywhere."),
            new("Set the per-user policies",
                @"DisableAIDataAnalysis and DisableClickToDo under HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\WindowsAI — Microsoft's documented settings. Recall stops saving snapshots and the Click to Do shortcut does nothing."),
            new("Existing snapshots",
                "Snapshots Recall already saved stay until you delete them in Settings → Privacy & security → Recall & snapshots; this stops new ones."),
            new("Run it again to undo",
                "The two values are removed, and Recall and Click to Do go back to following their own switches in Settings. No administrator rights."),
        })
    {
        AppliedLabel = "Recall off",
        NotAppliedLabel = "Recall on",
        Unavailable = Availability.Recall,
    };

    public static readonly RegistryTweak TipsAndAds = new(
        Slug: "tips-and-ads",
        Category: ToolCategory.Privacy,
        Name: "Turn Off Tips, Suggestions & Ads",
        Summary: "Remove promoted apps from Start's Recommended section, tips, suggested content in Settings, and apps Windows installs by itself.",
        IconKey: "IconInfo",
        Values: new[]
        {
            User(Advanced, "Start_IrisRecommendations", 0, "Promotions in Start's Recommended section"),
            User(Advanced, "Start_AccountNotifications", 0, "Account notifications in Start"),
            User(ContentDelivery, "SystemPaneSuggestionsEnabled", 0, "Suggested apps in Start"),
            User(ContentDelivery, "SubscribedContent-338388Enabled", 0, "Occasional suggestions in Start"),
            User(ContentDelivery, "SubscribedContent-338389Enabled", 0, "Tips and tricks notifications"),
            User(ContentDelivery, "SubscribedContent-310093Enabled", 0, "\"Welcome experience\" after updates"),
            User(ContentDelivery, "SubscribedContent-353694Enabled", 0, "Suggested content in Settings"),
            User(ContentDelivery, "SubscribedContent-353696Enabled", 0, "More suggested content in Settings"),
            User(ContentDelivery, "SilentInstalledAppsEnabled", 0, "Promoted apps installed without asking"),
        },
        RestartsExplorer: true,
        AppliedText: "no promotions, tips or silently installed apps.",
        RevertedText: "Windows shows its suggestions again.",
        Steps: new ToolStep[]
        {
            new("What this switches off",
                "The promoted apps and \"recommendations\" in Start, the tips-and-tricks notifications, the full-screen welcome after big updates, the suggested content banners in Settings, and the promoted apps Windows installs by itself."),
            new("Set the values Settings uses",
                "Each is a per-user value under Explorer\\Advanced or the ContentDeliveryManager key — the same ones the individual switches in Settings → Personalization and Privacy flip, all set at once."),
            new("Apps already installed stay",
                "Promoted apps Windows installed before this stay installed; uninstall them from Settings → Apps. This stops new ones."),
            new("Restart Explorer, and undo by running again",
                "Explorer restarts so Start redraws. Running the tool again removes every value, returning each switch to Windows' default. No administrator rights."),
        })
    {
        AppliedLabel = "Ads off",
        NotAppliedLabel = "Ads on",
    };

    public static readonly RegistryTweak AdvertisingId = new(
        Slug: "advertising-id",
        Category: ToolCategory.Privacy,
        Name: "Turn Off Advertising ID & Tailored Experiences",
        Summary: "Stop apps using your advertising ID to track you, and Microsoft tailoring tips and ads from your diagnostic data.",
        IconKey: "IconPrivacy",
        Values: new[]
        {
            User(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, "Advertising ID for apps"),
            User(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, "Tailored experiences from diagnostic data"),
        },
        RestartsExplorer: false,
        AppliedText: "apps get no advertising ID, and nothing is tailored from diagnostic data.",
        RevertedText: "both follow Windows' defaults again.",
        Steps: new ToolStep[]
        {
            new("The advertising ID",
                "A per-user identifier apps can read to show you personalised ads and follow you across apps. Turning it off is the first switch in Settings → Privacy & security → General."),
            new("Tailored experiences",
                "Whether Microsoft uses your diagnostic data to personalise tips, ads and recommendations — the switch under Settings → Privacy & security → Diagnostics & feedback."),
            new("Set both, and undo by running again",
                "Two per-user values, applied immediately; no restart needed. Running the tool again removes them. No administrator rights."),
        })
    {
        AppliedLabel = "Tracking off",
        NotAppliedLabel = "Tracking on",
    };

    public static IReadOnlyList<RegistryTweak> All => new[] { BingSearch, TipsAndAds, AdvertisingId, Copilot, Recall };
}
