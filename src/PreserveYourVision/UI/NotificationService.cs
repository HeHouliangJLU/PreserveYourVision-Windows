using PreserveYourVision.Core;
using PreserveYourVision.Models;

namespace PreserveYourVision.UI;

/// <summary>
/// 通知窗口管理。预休息预警与 toast 模式的休息提醒均使用轻量自定义 WPF 窗口，
/// 不依赖 WinRT Toast / NuGet，也不会阻断用户操作。
/// </summary>
public sealed class NotificationService : IDisposable
{
    private ToastWindow? _preBreakWindow;
    private BreakKind _preBreakKind;
    private ToastWindow? _breakWindow;
    private ToastWindow? _infoWindow;

    public void ShowPreBreak(
        int seconds,
        BreakKind kind,
        bool allowActions,
        Action startNow,
        Action skip,
        Action postpone)
    {
        _preBreakKind = kind;
        CloseInfo();
        ClosePreBreak();

        var window = new ToastWindow();
        _preBreakWindow = window;

        var title = kind == BreakKind.Long ? "长休息即将开始" : "休息即将开始";

        if (allowActions)
        {
            window.ShowToast(
                title,
                BuildPreBreakMessage(seconds),
                seconds + 5,
                ("跳过本次", skip, true),
                ("推迟 5 分钟", postpone, true),
                ("立即休息", startNow, false));
        }
        else
        {
            window.ShowToast(
                title,
                BuildPreBreakMessage(seconds) + "\n本次休息不允许跳过。",
                seconds + 5,
                ("立即休息", startNow, false));
        }
    }

    public void UpdatePreBreak(int seconds)
    {
        if (_preBreakWindow is { IsClosed: false })
        {
            _preBreakWindow.UpdateMessage(BuildPreBreakMessage(seconds));
        }
    }

    public void HidePreBreak()
    {
        ClosePreBreak();
    }

    public void ShowBreakToast(BreakStartedEventArgs args, bool allowPostpone, Action postpone)
    {
        ClosePreBreak();
        CloseInfo();
        CloseBreakToast();

        var window = new ToastWindow();
        _breakWindow = window;

        var title = args.Kind == BreakKind.Long ? "起身活动时间" : "护眼休息时间";
        var message = args.Message + "\n" + BreakScheduler.FormatDuration(args.DurationSeconds) + " 后结束";

        if (allowPostpone)
        {
            window.ShowToast(
                title,
                message,
                args.DurationSeconds + 10,
                ("推迟 5 分钟", postpone, false));
        }
        else
        {
            window.ShowToast(title, message, args.DurationSeconds + 10);
        }
    }

    public void UpdateBreakToast(int remainingSeconds, string message)
    {
        if (_breakWindow is { IsClosed: false })
        {
            _breakWindow.UpdateMessage(
                message + "\n剩余 " + BreakScheduler.FormatDuration(remainingSeconds));
        }
    }

    public void HideBreakToast()
    {
        CloseBreakToast();
    }

    public void ShowInfo(string title, string message, int autoCloseSeconds = 6)
    {
        CloseInfo();
        var window = new ToastWindow();
        _infoWindow = window;
        window.ShowToast(title, message, autoCloseSeconds);
    }

    public void CloseAll()
    {
        ClosePreBreak();
        CloseBreakToast();
        CloseInfo();
    }

    private string BuildPreBreakMessage(int seconds)
    {
        var kindText = _preBreakKind == BreakKind.Long ? "长休息（起身活动）" : "短休息（远眺放松）";
        return $"将在 {Math.Max(0, seconds)} 秒后开始{kindText}。";
    }

    private void ClosePreBreak()
    {
        _preBreakWindow?.CloseNotification();
        _preBreakWindow = null;
    }

    private void CloseBreakToast()
    {
        _breakWindow?.CloseNotification();
        _breakWindow = null;
    }

    private void CloseInfo()
    {
        _infoWindow?.CloseNotification();
        _infoWindow = null;
    }

    public void Dispose()
    {
        CloseAll();
    }
}
