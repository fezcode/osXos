namespace OsXos.Tools.MacOS;

/// <summary>
/// Switches macOS between dark and light appearance through System Events — the
/// scripting face of the Appearance switch in System Settings, so the whole system
/// changes at once exactly as if you had clicked it there. A two-way toggle.
/// </summary>
public sealed class DarkModeTool : ITool
{
    /// <summary>Prints "Dark" in dark mode; in light mode the key is absent and it exits non-zero.</summary>
    public static readonly ShellCommand ReadCommand =
        new("defaults", "read", "-g", "AppleInterfaceStyle");

    public static ShellCommand WriteCommand(bool dark) =>
        new("osascript", "-e",
            $"tell application \"System Events\" to tell appearance preferences to set dark mode to {(dark ? "true" : "false")}");

    readonly IProcessRunner _runner;

    public DarkModeTool(IProcessRunner runner) => _runner = runner;

    public string Id => "macos.dark-mode";
    public OSKind Platform => OSKind.MacOS;
    public ToolCategory Category => ToolCategory.System;
    public string Name => "Switch Dark / Light Mode";
    public string Summary => "Flip macOS between dark and light appearance, the same as the switch in System Settings.";
    public string IconKey => "IconMoon";

    public string? Warning =>
        "The first time, macOS asks whether osXos may control System Events. Allow it, or the switch cannot be made — " +
        "the answer is kept in System Settings → Privacy & Security → Automation.";

    public bool IsDestructive => false;

    public IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the appearance macOS is using now",
            "defaults read -g AppleInterfaceStyle. It prints Dark in dark mode; in light mode the key does not exist and the command reports an error, which is simply what light mode looks like."),
        new("Ask System Events to switch it",
            "osascript tells System Events to set dark mode to the opposite value. This is the same setting System Settings → Appearance changes, applied the same way — every app that follows the system switches on the spot, and the menu bar and Dock with them."),
        new("Allow Automation once",
            "macOS asks, the first time, whether osXos may control System Events. That permission is what lets any app change the appearance; it is remembered, and can be withdrawn under Privacy & Security → Automation."),
        new("Only your account is affected",
            "Appearance is a per-user preference. No administrator rights, and nothing else about your setup changes. If Appearance is set to Auto, macOS may switch again at its next scheduled change."),
    };

    public static bool ParseDark(ProcessOutcome read) =>
        read.Ok && read.StdOut.Trim().Equals("Dark", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolPreview> InspectAsync(CancellationToken ct)
    {
        if (!_runner.Exists("osascript"))
            return ToolPreview.Blocked(
                "The osascript command could not be found. That is unexpected on macOS — it lives in /usr/bin.");

        var dark = ParseDark(await _runner.RunAsync(ReadCommand, ct).ConfigureAwait(false));
        var target = !dark;

        var items = new[]
        {
            new PreviewItem("Appearance", $"{Mode(dark)} → {Mode(target)}"),
            new PreviewItem("Will run", WriteCommand(target).Display),
        };

        return new ToolPreview(items, $"macOS is in {Mode(dark)} mode. Will switch to {Mode(target)} mode.");
    }

    static string Mode(bool dark) => dark ? "dark" : "light";

    public async Task<ToolResult> RunAsync(
        ToolPreview preview, CancellationToken ct, IProgress<ToolProgress>? progress = null)
    {
        var target = !ParseDark(await _runner.RunAsync(ReadCommand, ct).ConfigureAwait(false));
        var write = WriteCommand(target);
        var outcome = await _runner.RunAsync(write, ct).ConfigureAwait(false);

        if (!outcome.Ok)
        {
            // -1743 is errAEEventNotPermitted: the Automation prompt was declined, or
            // was declined once before and macOS will not ask again by itself.
            if (outcome.StdErr.Contains("-1743", StringComparison.Ordinal))
                return ToolResult.Failure("macOS did not allow the switch",
                    "osXos is not allowed to control System Events.",
                    "Turn it on under System Settings → Privacy & Security → Automation, then run this again.");

            return ToolResult.Failure("Could not change the appearance",
                write.Display, $"exit code {outcome.ExitCode}", outcome.Message);
        }

        return ToolResult.Success(
            $"macOS is now in {Mode(target)} mode",
            write.Display,
            "Run this tool again to switch back.");
    }
}
