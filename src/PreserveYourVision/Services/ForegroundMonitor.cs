using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PreserveYourVision.Services;

public readonly record struct ForegroundInfo(
    IntPtr Handle,
    uint ProcessId,
    string ProcessName,
    string WindowTitle,
    string ClassName,
    bool IsFullscreen);

/// <summary>
/// 轮询前景窗口（DESIGN.md §8.2.3）：判断是否全屏/独占，并给出进程名与标题，
/// 供自动暂停检测（FR-10）使用。
/// </summary>
public sealed class ForegroundMonitor
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private readonly Dictionary<uint, (string Name, DateTime CachedAt)> _processNameCache = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    public ForegroundInfo? GetForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId)
            {
                return null;
            }

            var titleBuilder = new StringBuilder(512);
            GetWindowTextW(hwnd, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString();

            var classBuilder = new StringBuilder(256);
            GetClassNameW(hwnd, classBuilder, classBuilder.Capacity);
            var className = classBuilder.ToString();

            // 桌面、任务栏、Windows Shell 自身不算全屏应用。
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "Windows.UI.Core.CoreWindow")
            {
                return null;
            }

            bool fullscreen = IsFullscreen(hwnd);

            return new ForegroundInfo(
                hwnd,
                pid,
                GetProcessName(pid),
                title,
                className,
                fullscreen);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFullscreen(IntPtr hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect))
            {
                return false;
            }

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var info = new MONITORINFO
            {
                cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
            };

            if (!GetMonitorInfoW(monitor, ref info))
            {
                return false;
            }

            const int tolerance = 2;
            bool coversMonitor =
                rect.Left <= info.rcMonitor.Left + tolerance &&
                rect.Top <= info.rcMonitor.Top + tolerance &&
                rect.Right >= info.rcMonitor.Right - tolerance &&
                rect.Bottom >= info.rcMonitor.Bottom - tolerance;

            if (!coversMonitor)
            {
                return false;
            }

            long style = GetWindowLongW(hwnd, GWL_STYLE) & 0xFFFFFFFFL;
            bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;
            return !hasCaption;
        }
        catch
        {
            return false;
        }
    }

    private string GetProcessName(uint pid)
    {
        var now = DateTime.UtcNow;
        if (_processNameCache.TryGetValue(pid, out var cached) &&
            (now - cached.CachedAt).TotalSeconds < 10)
        {
            return cached.Name;
        }

        string name = "";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch
        {
            // 系统进程或无权限访问时忽略。
        }

        if (_processNameCache.Count > 256)
        {
            _processNameCache.Clear();
        }

        _processNameCache[pid] = (name, now);
        return name;
    }
}
