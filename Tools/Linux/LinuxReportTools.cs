namespace OsXos.Tools.Linux;

public sealed class NetworkInterfacesTool : CommandReportTool
{
    public NetworkInterfacesTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("ip", "address", "show");
    public override string Id => "linux.network-interfaces";
    public override OSKind Platform => OSKind.Linux;
    public override ToolCategory Category => ToolCategory.Network;
    public override string Name => "Network Interfaces Report";
    public override string Summary => "Inspect local interface addresses, flags and link state — read-only.";
    public override string IconKey => "IconNetwork";
    protected override string MissingCommandHint => "The ip utility is supplied by iproute2 on most distributions; check its installation and PATH.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read interface addresses", "ip address show lists IPv4 and IPv6 addresses for interfaces visible to this session, including loopback and virtual interfaces. It sends no connectivity probes."),
        new("Inspect interface state", "Review the interface flags, link state and address scopes. The report does not include DNS configuration or prove that the internet is reachable."),
        new("Change nothing", "No addresses, routes or connections are modified. Result repeats the snapshot shown in Review."),
    };
}

public sealed class NetworkConnectionsTool : CommandReportTool
{
    public NetworkConnectionsTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("ss", "-t", "-u", "-a", "-n", "-p");
    public override string Id => "linux.network-connections";
    public override OSKind Platform => OSKind.Linux;
    public override ToolCategory Category => ToolCategory.Network;
    public override string Name => "Connections & Listening Ports";
    public override string Summary => "List TCP/UDP sockets and the owning processes your account can see — read-only.";
    public override string IconKey => "IconNetwork";
    protected override string MissingCommandHint => "The ss utility is supplied by iproute2 on most distributions; check its installation and PATH.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read local sockets", "ss -t -u -a -n -p lists TCP and UDP sockets, both listening and non-listening, once. Numeric output avoids name lookups; no remote endpoints are contacted."),
        new("Read available process ownership", "Process names and PIDs are shown only where your account can read them. A missing owner does not mean a socket has no owner; the tool does not request root privileges."),
        new("Leave connections alone", "No sockets are closed and no processes are stopped. This snapshot does not test firewall rules or internet exposure."),
    };
}

public sealed class DiskSpaceTool : CommandReportTool
{
    public DiskSpaceTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("df", "-h", "-l");
    public override string Id => "linux.disk-space";
    public override OSKind Platform => OSKind.Linux;
    public override ToolCategory Category => ToolCategory.System;
    public override string Name => "Local Disk Space Report";
    public override string Summary => "Check used and available space on mounted local filesystems — read-only.";
    public override string IconKey => "IconInfo";
    protected override string MissingCommandHint => "df is normally supplied by coreutils; check its installation and PATH.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read local filesystem usage", "df -h -l reports mounted local filesystems in readable units. Network filesystems are excluded, and the command does not scan directories or delete files."),
        new("Compare available space", "Review each filesystem's size, used space, available space and mount point. Reserved blocks can make available space smaller than size minus used; memory-backed mounts may also appear."),
        new("Keep a snapshot", "Unmounted disks are not included. Inspect again after a cleanup to see the new figures; Result keeps the original Review snapshot."),
    };
}

public sealed class MemoryUsageTool : CommandReportTool
{
    public MemoryUsageTool(IProcessRunner runner) : base(runner) { }
    protected override ShellCommand Command { get; } = new("free", "-h");
    public override string Id => "linux.memory-usage";
    public override OSKind Platform => OSKind.Linux;
    public override ToolCategory Category => ToolCategory.System;
    public override string Name => "Memory & Swap Report";
    public override string Summary => "Check RAM, cache, available memory and swap usage — read-only.";
    public override string IconKey => "IconInfo";
    protected override string MissingCommandHint => "free is normally supplied by procps or procps-ng; check its installation and PATH.";
    public override IReadOnlyList<ToolStep> Steps { get; } = new ToolStep[]
    {
        new("Read memory counters", "free -h reads the kernel's memory counters and displays RAM and swap totals in readable units. It does not allocate test memory or change swap settings."),
        new("Compare free and available memory", "Linux uses otherwise idle RAM for caches. The available column estimates memory usable by new applications without swapping and is usually more useful than free alone."),
        new("Leave caches intact", "No caches are dropped and no processes are stopped. In a container, these counters may reflect the host rather than the container's memory limit."),
    };
}
