namespace OsXos.Tools.Windows;

/// <summary>Windows reports resolve executables from the system directory.</summary>
public abstract class WindowsReportTool : CommandReportTool
{
    protected WindowsReportTool(IProcessRunner runner) : base(runner) { }
    public override OSKind Platform => OSKind.Windows;
    protected override string MissingCommandHint => "Check the Windows system folder.";

    protected static ShellCommand SystemCommand(string file, params string[] args) =>
        new(Path.Combine(Environment.SystemDirectory, file), args);
}

public sealed class NetworkConfigurationTool : WindowsReportTool
{
    public NetworkConfigurationTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = SystemCommand("ipconfig.exe", "/all");
    public override string Id => "windows.network-configuration";
    public override ToolCategory Category => ToolCategory.Network;
    public override string Name => "Network Configuration Report";
    public override string Summary => "Inspect adapter addresses, gateways, DNS servers and DHCP settings — read-only.";
    public override string IconKey => "IconNetwork";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read adapter configuration", "ipconfig /all lists the local TCP/IP configuration, including disconnected and virtual adapters. It does not renew leases or change DNS settings."),
        new("Find the connection you use", "Review the adapter's IP address, default gateway, DHCP and DNS servers when diagnosing a connection problem. This reports configuration, not whether the internet is reachable."),
        new("Keep a snapshot", "The Review stage contains Windows' output in its own language. Result repeats that snapshot; inspect again after changing a connection to see updated values."),
    };
}

public sealed class NetworkConnectionsTool : WindowsReportTool
{
    public NetworkConnectionsTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = SystemCommand("netstat.exe", "-a", "-n", "-o");
    public override string Id => "windows.network-connections";
    public override ToolCategory Category => ToolCategory.Network;
    public override string Name => "Connections & Listening Ports";
    public override string Summary => "See TCP connections, TCP/UDP listening ports and owning process IDs — read-only.";
    public override string IconKey => "IconNetwork";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the local connection table", "netstat -a -n -o lists TCP connections and TCP/UDP listening ports once. Numeric addresses avoid hostname lookups; no remote systems are contacted."),
        new("Match a port to a process", "The last column is the process ID (PID). Match it with the PID column on Task Manager's Details tab. Connections and PIDs can change after this snapshot."),
        new("Interpret listening ports", "A listening port means a program is waiting for traffic. This report does not test firewall rules or internet exposure, and does not close connections or stop processes."),
    };
}

public sealed class SleepStatesTool : WindowsReportTool
{
    public SleepStatesTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = SystemCommand("powercfg.exe", "/availablesleepstates");
    public override string Id => "windows.sleep-states";
    public override ToolCategory Category => ToolCategory.System;
    public override string Name => "Sleep States Report";
    public override string Summary => "Check available sleep and hibernation states, with Windows' reasons for unavailable modes.";
    public override string IconKey => "IconInfo";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Ask Windows which states are available", "powercfg /availablesleepstates reports supported sleep states, hibernation and related power capabilities on this PC."),
        new("Read why a mode is unavailable", "Windows includes reasons such as firmware support or disabled hibernation. Availability is different from the sleep timeout or active power plan."),
        new("Leave power settings alone", "This report does not enable hibernation, change a power plan or put the PC to sleep. Review and Result show the same captured output."),
    };
}

public sealed class InstalledDriversTool : WindowsReportTool
{
    public InstalledDriversTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = SystemCommand("driverquery.exe", "/fo", "list");
    public override string Id => "windows.installed-drivers";
    public override ToolCategory Category => ToolCategory.System;
    public override string Name => "Installed Drivers Report";
    public override string Summary => "List installed driver modules and their properties without changing drivers.";
    public override string IconKey => "IconInfo";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read the local driver inventory", "driverquery /fo list asks this PC for its installed device drivers. Each module is shown with the properties Windows returns, in list form."),
        new("Use the inventory for troubleshooting", "Compare module and display names with the driver you are investigating. This is an inventory, not a driver health check or a search for updates."),
        new("Change nothing", "No drivers are downloaded, installed, disabled or removed. If Windows denies access to the inventory, the report shows that error without requesting elevation."),
    };
}
