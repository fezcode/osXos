using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OsXos.Tools.Windows;

/// <summary>
/// The Windows taskbar as something osXos can hide, behind an interface so the hide
/// tool's decisions are tested without making a real taskbar disappear.
/// </summary>
public interface ITaskbarController
{
    /// <summary>Whether Explorer's taskbar window exists at all right now.</summary>
    bool Exists { get; }

    /// <summary>Whether the primary taskbar window is currently hidden.</summary>
    bool IsHidden { get; }

    /// <summary>Windows' own "automatically hide the taskbar" setting.</summary>
    bool AutoHide { get; set; }

    /// <summary>
    /// Shows or hides every taskbar window — the primary one and the one on each
    /// extra monitor. Returns how many windows it changed.
    /// </summary>
    int SetVisible(bool visible);

    /// <summary>
    /// The handles of every taskbar window right now. A handle osXos has not seen
    /// before means Explorer made a new taskbar — it restarted, or a monitor arrived.
    /// </summary>
    IReadOnlyList<long> Windows();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsTaskbarController : ITaskbarController
{
    const string Primary = "Shell_TrayWnd";
    const string Secondary = "Shell_SecondaryTrayWnd";

    public bool Exists => FindWindow(Primary, null) != IntPtr.Zero;

    public bool IsHidden
    {
        get
        {
            var tray = FindWindow(Primary, null);
            return tray != IntPtr.Zero && !IsWindowVisible(tray);
        }
    }

    /// <summary>
    /// Read and written through SHAppBarMessage — the documented appbar API, which
    /// applies the change live and has Explorer save it exactly as the Settings
    /// switch does, so it survives sign-out and restarts on its own.
    /// </summary>
    public bool AutoHide
    {
        get
        {
            var data = NewData(FindWindow(Primary, null));
            return ((int)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
        }
        set
        {
            var data = NewData(FindWindow(Primary, null));
            data.lParam = value ? ABS_AUTOHIDE : ABS_ALWAYSONTOP;
            SHAppBarMessage(ABM_SETSTATE, ref data);
        }
    }

    public IReadOnlyList<long> Windows() => TaskbarWindows().Select(h => (long)h).ToList();

    public int SetVisible(bool visible)
    {
        var changed = 0;
        foreach (var hwnd in TaskbarWindows())
        {
            ShowWindow(hwnd, visible ? SW_SHOWNA : SW_HIDE);
            changed++;
        }
        return changed;
    }

    static IEnumerable<IntPtr> TaskbarWindows()
    {
        var primary = FindWindow(Primary, null);
        if (primary != IntPtr.Zero) yield return primary;

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, Secondary, null)) != IntPtr.Zero)
            yield return secondary;
    }

    static APPBARDATA NewData(IntPtr hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = hwnd,
    };

    const uint ABM_GETSTATE = 0x04;
    const uint ABM_SETSTATE = 0x0A;
    const int ABS_AUTOHIDE = 0x01;
    const int ABS_ALWAYSONTOP = 0x02;
    const int SW_HIDE = 0;
    const int SW_SHOWNA = 8;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    static extern IntPtr SHAppBarMessage(uint message, ref APPBARDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);
}

/// <summary>
/// What <c>osXos --hide-taskbar</c> does: wait for Explorer to create the taskbar,
/// hide it, then keep it hidden until told to stop. It runs from the sign-in entry
/// Hide the Taskbar adds, and is started straight away when that tool runs.
///
/// Hiding once is not enough, because every Explorer restart — Restart Explorer,
/// an update, a crash — makes a brand-new taskbar window, and so does plugging in
/// a monitor. The keeper watches for taskbar windows it has not seen and hides
/// those. It never re-hides a window it already hid, so it cannot fight Windows or
/// the user over one that was deliberately shown.
/// </summary>
public static class TaskbarKeeper
{
    public const string Argument = "--hide-taskbar";

    static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    /// <param name="waitForStop">
    /// Waits up to the given time and returns true if the keeper has been told to
    /// stop. The real one waits on a named event; tests count calls instead.
    /// </param>
    public static void Run(ITaskbarController taskbar, Func<TimeSpan, bool> waitForStop)
    {
        // At sign-in Explorer may not have drawn the taskbar yet. Give it a minute,
        // then keep watching anyway: a taskbar that turns up late still gets hidden.
        var seen = new HashSet<long>();

        while (true)
        {
            var current = taskbar.Windows();
            if (current.Any(h => !seen.Contains(h)))
            {
                taskbar.AutoHide = true;
                taskbar.SetVisible(false);
            }

            // Forget windows that have gone, so a reused handle is treated as new.
            seen = current.ToHashSet();

            if (waitForStop(Step)) return;
        }
    }
}

/// <summary>
/// The keeper as a process: whether one is running, starting one, and telling it to
/// stop. One per signed-in user, enforced with a named mutex, and stopped with a
/// named event rather than by killing a process osXos would have to find.
/// </summary>
public interface ITaskbarKeeperProcess
{
    bool IsRunning { get; }
    void Start(string exe);
    void Stop();
}

[SupportedOSPlatform("windows")]
public sealed class WindowsTaskbarKeeperProcess : ITaskbarKeeperProcess
{
    const string MutexName = @"Local\osXos.HideTaskbar.Keeper";
    const string StopName = @"Local\osXos.HideTaskbar.Stop";

    public bool IsRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(MutexName, out var mutex)) return false;
            mutex.Dispose();
            return true;
        }
    }

    public void Start(string exe)
    {
        if (IsRunning) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, TaskbarKeeper.Argument)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }

    public void Stop()
    {
        if (EventWaitHandle.TryOpenExisting(StopName, out var stop))
        {
            using (stop) stop.Set();
        }
    }

    /// <summary>
    /// The body of <c>osXos --hide-taskbar</c>. Exits at once if a keeper is already
    /// running for this user, so a second sign-in entry or a double start is harmless.
    /// </summary>
    public static void RunHere()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (!created) return;

        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName);
        TaskbarKeeper.Run(new WindowsTaskbarController(), wait => stop.WaitOne(wait));
    }
}
