namespace OsXos.Tools.MacOS;

public sealed class NetworkInterfacesTool : CommandReportTool
{
    public NetworkInterfacesTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("/sbin/ifconfig", "-a");
    public override string Id => "macos.network-interfaces";
    public override OSKind Platform => OSKind.MacOS;
    public override ToolCategory Category => ToolCategory.Network;
    public override string Name => "Network Interfaces Report";
    public override string Summary => "Inspect interface addresses, link status and MTU settings — read-only.";
    public override string IconKey => "IconNetwork";
    protected override string MissingCommandHint => "ifconfig is normally supplied by macOS in /sbin.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read every interface", "ifconfig -a lists local network interfaces, including loopback, disconnected interfaces and virtual adapters. It does not change their addresses or bring connections up or down."),
        new("Check addresses and link status", "Look for IPv4 and IPv6 addresses, interface flags, MTU and media status. This is configuration information, not an internet connectivity test."),
        new("Keep the snapshot", "Review shows the command's output. Result keeps the same snapshot; inspect again after connecting or disconnecting a network."),
    };
}

public sealed class DnsConfigurationTool : CommandReportTool
{
    public DnsConfigurationTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("/usr/sbin/scutil", "--dns");
    public override string Id => "macos.dns-configuration";
    public override OSKind Platform => OSKind.MacOS;
    public override ToolCategory Category => ToolCategory.Network;
    public override string Name => "DNS Configuration Report";
    public override string Summary => "See configured DNS resolvers, search domains and interface scopes — read-only.";
    public override string IconKey => "IconNetwork";
    protected override string MissingCommandHint => "scutil is normally supplied by macOS in /usr/sbin.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the resolver configuration", "scutil --dns displays the DNS configuration macOS currently holds. It reads local settings without sending test queries or flushing the DNS cache."),
        new("Inspect scoped resolvers", "VPNs and individual interfaces can have their own DNS servers and search domains. Several resolver entries are normal and do not by themselves indicate a problem."),
        new("Leave DNS unchanged", "This report does not select a DNS provider or prove that a server is reachable. Review and Result display the same captured configuration."),
    };
}

public sealed class BatteryStatusTool : CommandReportTool
{
    public BatteryStatusTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("/usr/bin/pmset", "-g", "batt");
    public override string Id => "macos.battery-status";
    public override OSKind Platform => OSKind.MacOS;
    public override ToolCategory Category => ToolCategory.System;
    public override string Name => "Battery & Power Source Report";
    public override string Summary => "Check the power source, battery charge and reported time remaining — read-only.";
    public override string IconKey => "IconInfo";
    protected override string MissingCommandHint => "pmset is normally supplied by macOS in /usr/bin.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the current power source", "pmset -g batt reports the power source and any batteries or UPS devices macOS exposes. A desktop Mac without a battery may only report AC power."),
        new("Read the battery estimate", "Charge, charging status and time estimates appear when available. The estimate changes with workload; this is not a battery health or capacity test."),
        new("Keep power settings unchanged", "No charging, sleep or energy settings are changed. Inspect again to update the snapshot."),
    };
}

public sealed class SleepAssertionsTool : CommandReportTool
{
    public SleepAssertionsTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("/usr/bin/pmset", "-g", "assertions");
    public override string Id => "macos.sleep-assertions";
    public override OSKind Platform => OSKind.MacOS;
    public override ToolCategory Category => ToolCategory.System;
    public override string Name => "Sleep Prevention Report";
    public override string Summary => "See the power assertions that may keep your Mac or its display awake.";
    public override string IconKey => "IconInfo";
    protected override string MissingCommandHint => "pmset is normally supplied by macOS in /usr/bin.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read current power assertions", "pmset -g assertions takes one snapshot of power assertions and their owners. Assertions can request that the system or display stay awake."),
        new("Identify the app involved", "The report includes owning processes where macOS provides them. An assertion can be legitimate, such as media playback or a transfer, and does not by itself mean an app is broken."),
        new("Leave applications running", "No process is stopped and no assertion is removed. Inspect again after closing an app yourself to see whether its assertion disappeared."),
    };
}
