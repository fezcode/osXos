using OsXos.Tools;
using OsXos.Tools.Windows;

namespace OsXos.Tests;

/// <summary>
/// The Windows tool set built entirely from fakes. Several of these tools delete
/// registry keys and empty the Recycle Bin; none of them should be constructed
/// against the real machine just to be counted or listed.
/// </summary>
public static class TestCatalog
{
    public static IReadOnlyList<ITool> Windows(
        IProcessRunner? runner = null,
        IRegistryAccess? registry = null,
        IRecycleBin? recycleBin = null,
        IShellController? shell = null)
    {
        runner ??= new FakeRunner();
        return ToolCatalog.Windows(
            runner,
            new FakeExplorerSettings(),
            registry ?? new FakeRegistry(),
            recycleBin ?? new FakeRecycleBin(),
            new ElevationService(runner),
            shell ?? new FakeShellController());
    }
}
