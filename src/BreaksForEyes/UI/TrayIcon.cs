using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BreaksForEyes.Models;

namespace BreaksForEyes.UI;

/// <summary>
/// 托盘常驻图标与右键菜单（FR-4）。ToolTipText 实时显示倒计时；
/// 左键点击打开菜单，右键由 NotifyIcon 默认处理。
/// 使用 WinForms NotifyIcon，零额外 NuGet 依赖。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _snoozeItem;
    private readonly ToolStripMenuItem _immediateItem;
    private readonly ToolStripMenuItem _resumeItem;
    private readonly Icon _icon;
    private string _lastToolTip = "";

    public TrayIcon(
        Action<TimeSpan?> snooze,
        Action immediateBreak,
        Action resume,
        Action openSettings,
        Action showAbout,
        Action exit)
    {
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false
        };

        _statusItem = new ToolStripMenuItem("Breaks for Eyes 正在启动…")
        {
            Enabled = false
        };
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());

        _snoozeItem = new ToolStripMenuItem("暂停休息");
        AddSnoozeItem("30 分钟", TimeSpan.FromMinutes(30), snooze);
        AddSnoozeItem("1 小时", TimeSpan.FromHours(1), snooze);
        AddSnoozeItem("2 小时", TimeSpan.FromHours(2), snooze);
        AddSnoozeItem("4 小时", TimeSpan.FromHours(4), snooze);
        AddSnoozeItem("当天剩余时间", null, snooze);
        _menu.Items.Add(_snoozeItem);

        _immediateItem = new ToolStripMenuItem("立即休息");
        _immediateItem.Click += (_, _) => InvokeSafely(immediateBreak);
        _menu.Items.Add(_immediateItem);

        _resumeItem = new ToolStripMenuItem("恢复计时")
        {
            Enabled = false
        };
        _resumeItem.Click += (_, _) => InvokeSafely(resume);
        _menu.Items.Add(_resumeItem);

        _menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("设置…");
        settingsItem.Click += (_, _) => InvokeSafely(openSettings);
        _menu.Items.Add(settingsItem);

        var aboutItem = new ToolStripMenuItem("关于");
        aboutItem.Click += (_, _) => InvokeSafely(showAbout);
        _menu.Items.Add(aboutItem);

        _menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => InvokeSafely(exit);
        _menu.Items.Add(exitItem);

        _icon = CreateAppIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Breaks for Eyes",
            Visible = true,
            ContextMenuStrip = _menu
        };

        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                _menu.Show(Cursor.Position);
            }
        };
    }

    public void UpdateStatus(SchedulerStatus status)
    {
        _statusItem.Text = string.IsNullOrWhiteSpace(status.Text) ? "Breaks for Eyes" : status.Text;
        _snoozeItem.Enabled = status.CanSnooze;
        _immediateItem.Enabled = status.CanStartBreak;
        _resumeItem.Enabled = status.CanResume;

        var toolTip = "Breaks for Eyes · " + status.Text;
        if (toolTip.Length > 120)
        {
            toolTip = toolTip[..120];
        }

        if (!string.Equals(toolTip, _lastToolTip, StringComparison.Ordinal))
        {
            _lastToolTip = toolTip;
            try
            {
                _notifyIcon.Text = toolTip;
            }
            catch
            {
                // 某些系统对 ToolTip 长度更严格，失败不影响托盘图标。
            }
        }
    }

    public void ShowInfo(string title, string message)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
        }
        catch
        {
            // 通知中心不可用时静默失败。
        }
    }

    private void AddSnoozeItem(string label, TimeSpan? duration, Action<TimeSpan?> snooze)
    {
        var item = new ToolStripMenuItem(label);
        item.Click += (_, _) =>
        {
            try
            {
                snooze(duration);
            }
            catch
            {
                // 菜单动作异常不应导致托盘进程退出。
            }
        };
        _snoozeItem.DropDownItems.Add(item);
    }

    private static void InvokeSafely(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // 菜单动作异常不应导致托盘进程退出。
        }
    }

    private static Icon CreateAppIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var backgroundBrush = new SolidBrush(Color.FromArgb(255, 13, 86, 78));
            graphics.FillEllipse(backgroundBrush, 0, 0, 31, 31);

            using var eyeBrush = new SolidBrush(Color.White);
            graphics.FillEllipse(eyeBrush, 6, 11, 20, 11);

            using var pupilBrush = new SolidBrush(Color.FromArgb(255, 9, 53, 48));
            graphics.FillEllipse(pupilBrush, 13, 12, 7, 7);

            using var highlightBrush = new SolidBrush(Color.White);
            graphics.FillEllipse(highlightBrush, 15, 13, 2, 2);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(handle);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        catch
        {
            // 忽略。
        }

        _menu.Dispose();
        _icon.Dispose();
    }
}
