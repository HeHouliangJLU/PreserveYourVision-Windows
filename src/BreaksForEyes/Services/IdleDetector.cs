using System.Runtime.InteropServices;

namespace BreaksForEyes.Services;

/// <summary>
/// 空闲检测（FR-9）：通过 Win32 GetLastInputInfo 判断用户是否仍在操作电脑。
/// 工作计时只累计活跃时间。
/// </summary>
public sealed class IdleDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>返回距离最后一次键盘/鼠标输入的秒数；API 失败时返回 0（视为活跃）。</summary>
    public double GetIdleSeconds()
    {
        try
        {
            var info = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };

            if (!GetLastInputInfo(ref info))
            {
                return 0;
            }

            // dwTime 与 Environment.TickCount 都是 32 位回绕计数，做无符号减法即可处理回绕。
            uint now = unchecked((uint)Environment.TickCount);
            uint idleMilliseconds = unchecked(now - info.dwTime);
            return idleMilliseconds / 1000.0;
        }
        catch
        {
            return 0;
        }
    }
}
