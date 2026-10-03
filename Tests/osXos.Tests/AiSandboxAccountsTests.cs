using OsXos.Tools;
using OsXos.Tools.Windows;
using Xunit;

namespace OsXos.Tests;

/// <summary>
/// Remove AI Sandbox Accounts. Accounts are never touched here: the account listing
/// is scripted, elevation is a stand-in that records the script it was handed, and
/// the "after" listing is scripted too.
/// </summary>
public class AiSandboxAccountsTests
{
    const string Both = """
        user|CodexSandboxOffline|S-1-5-21-1-1001|True|10/3/2026 4:59:41 PM|
        user|CodexSandboxOnline|S-1-5-21-1-1002|True||C:\Users\CodexSandboxOnline
        group|CodexSandboxUsers
        """;

    /// <summary>A runner whose account listing changes after the elevated run.</summary>
    sealed class Listing : IProcessRunner
    {
        public string Before { get; set; } = Both;
        public string After { get; set; } = "";
        public bool Removed { get; set; }

        public Task<ProcessOutcome> RunAsync(ShellCommand command, CancellationToken ct) =>
            Task.FromResult(new ProcessOutcome(0, Removed ? After : Before, ""));

        public bool Exists(string file) => true;
    }

    sealed class Elevation : IElevationService
    {
        readonly Listing _listing;
        public Elevation(Listing listing) => _listing = listing;
        public List<string> Scripts { get; } = new();
        public int ExitCode { get; set; }
        public bool IsElevated => false;
        public bool CanElevate => true;
        public string PromptDescription => "fake";

        public Task<ProcessOutcome> RunElevatedAsync(ShellCommand command, CancellationToken ct)
        {
            Scripts.Add(File.ReadAllText(command.Args[^1]));
            if (ExitCode == 0) _listing.Removed = true;
            return Task.FromResult(new ProcessOutcome(ExitCode, "", ""));
        }
    }

    static (AiSandboxAccountsTool Tool, Listing Listing, Elevation Elevation) Build(bool codexRunning = false)
    {
        var listing = new Listing();
        var elevation = new Elevation(listing);
        var tool = new AiSandboxAccountsTool(listing, elevation, () => codexRunning,
            () => Path.Combine(Path.GetTempPath(), $"osxos-test-{Guid.NewGuid():N}.ps1"));
        return (tool, listing, elevation);
    }

    [Fact]
    public async Task It_lists_each_codex_account_with_its_state_and_profile()
    {
        var (tool, _, _) = Build();

        var preview = await tool.InspectAsync(default);

        Assert.True(preview.NeedsElevation);
        Assert.Equal("2 sandbox accounts and their group · needs administrator rights", preview.Summary);
        Assert.Contains(preview.Items, i => i.Label == "Codex · CodexSandboxOffline"
            && i.Detail!.Contains("last sign-in 10/3/2026", StringComparison.Ordinal) && i.Detail.Contains("no profile folder", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Label == "Codex · CodexSandboxOnline"
            && i.Detail!.Contains(@"profile C:\Users\CodexSandboxOnline will be removed", StringComparison.Ordinal));
        Assert.Contains(preview.Items, i => i.Label == "Codex · CodexSandboxUsers (group)");
    }

    [Fact]
    public void Only_known_names_are_ever_accepted_whatever_the_listing_says()
    {
        var found = AiSandboxAccountsTool.Parse("user|ahmed|S-1-5-21-1-1000|True||C:\\Users\\ahmed\ngroup|Administrators\n" + Both);

        Assert.Equal(new[] { "CodexSandboxOffline", "CodexSandboxOnline", "CodexSandboxUsers" }, found.Select(f => f.Known.Name));
    }

    [Fact]
    public void The_known_list_is_exactly_codexs_three()
    {
        // A name joins only once the product that creates it is identified.
        Assert.Equal(
            new[] { "CodexSandboxOffline", "CodexSandboxOnline", "CodexSandboxUsers" },
            AiSandboxAccountsTool.KnownAccounts.Select(k => k.Name));
    }

    [Fact]
    public void The_script_removes_profiles_then_accounts_then_the_group()
    {
        var script = AiSandboxAccountsTool.RemovalScript(AiSandboxAccountsTool.Parse(Both));

        var profile = script.IndexOf("Win32_UserProfile -Filter \"SID='S-1-5-21-1-1002'\"", StringComparison.Ordinal);
        var user = script.IndexOf("Remove-LocalUser -Name 'CodexSandboxOnline'", StringComparison.Ordinal);
        var group = script.IndexOf("Remove-LocalGroup -Name 'CodexSandboxUsers'", StringComparison.Ordinal);
        Assert.True(profile >= 0 && user > profile && group > user, script);
        Assert.Contains("Remove-LocalUser -Name 'CodexSandboxOffline'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ahmed", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_result_reports_what_windows_says_is_gone()
    {
        var (tool, listing, elevation) = Build();
        listing.After = "user|CodexSandboxOffline|S-1-5-21-1-1001|True||\n"; // in use, so it stayed

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.True(result.Ok);
        Assert.Single(elevation.Scripts);
        Assert.Contains(result.Lines, l => l.StartsWith(@"Removed CodexSandboxOnline and its profile C:\Users\CodexSandboxOnline", StringComparison.Ordinal));
        Assert.Contains(result.Lines, l => l.StartsWith("Still there: CodexSandboxOffline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_declined_prompt_changes_nothing()
    {
        var (tool, listing, elevation) = Build();
        elevation.ExitCode = 1223;

        var result = await tool.RunAsync(await tool.InspectAsync(default), default);

        Assert.False(result.Ok);
        Assert.Equal("Cancelled", result.Headline);
        Assert.False(listing.Removed);
    }

    [Fact]
    public async Task It_warns_when_codex_is_running_and_blocks_when_nothing_is_there()
    {
        var (running, _, _) = Build(codexRunning: true);
        Assert.Equal("Codex is running", (await running.InspectAsync(default)).Items[0].Label);

        var (empty, listing, _) = Build();
        listing.Before = "";
        var preview = await empty.InspectAsync(default);
        Assert.False(preview.CanRun);
        Assert.Equal("None", (await empty.ReadStateAsync(default)).Label);
    }

    [Fact]
    public async Task The_state_counts_the_accounts()
    {
        var (tool, _, _) = Build();
        Assert.Equal("2 accounts", (await tool.ReadStateAsync(default)).Label);
    }

    [Fact]
    public void It_lives_in_ai_assistants_and_asks_for_administrator()
    {
        var tool = TestCatalog.Windows().Single(t => t.Id == "windows.ai-sandbox-accounts");
        Assert.Equal(ToolCategory.AI, tool.Category);
        Assert.True(tool.RequiresElevation);
        Assert.True(tool.IsDestructive);
        Assert.False(string.IsNullOrWhiteSpace(tool.Warning));
    }
}
