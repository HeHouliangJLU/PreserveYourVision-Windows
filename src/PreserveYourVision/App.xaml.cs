using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using PreserveYourVision.Core;
using PreserveYourVision.Models;
using PreserveYourVision.Services;
using PreserveYourVision.UI;

namespace PreserveYourVision;

/// <summary>
/// 应用入口（AppController）：单实例、服务初始化、UI 与调度器接线、
/// 1 秒 tick 与优雅退出。
/// </summary>
public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;
    private SettingsService? _settingsService;
    private Settings _settings = Settings.CreateDefault();
    private StatsService? _statsService;
    private SoundService? _soundService;
    private BreakScheduler? _scheduler;
    private TrayIcon? _trayIcon;
    private OverlayManager? _overlayManager;
    private NotificationService? _notificationService;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private DispatcherTimer? _tickTimer;

    private readonly Stopwatch _tickWatch = new();
    private TimeSpan _lastTickTime;
    private int _tickCount;
    private string _activeReminderMode = "";
    private string _lastBreakMessage = "";
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstance = new SingleInstance();
        if (!_singleInstance.TryAcquire())
        {
            // 第二个实例：唤醒已有实例打开设置后退出。
            _singleInstance.SignalExistingInstance();
            Shutdown(0);
            return;
        }

        _singleInstance.StartSignalListener(Dispatcher, OpenSettings);

        _settingsService = new SettingsService();
        _settings = _settingsService.Load(out bool firstRun);

        _statsService = new StatsService();
        _statsService.Load();

        _soundService = new SoundService();
        _scheduler = new BreakScheduler(
            _settings,
            _statsService,
            new IdleDetector(),
            new AutoPauseDetector(),
            _soundService);

        _notificationService = new NotificationService();
        _overlayManager = new OverlayManager(OnOverlaySkip, OnOverlayPostpone);

        _trayIcon = new TrayIcon(
            SnoozeFromTray,
            OnTrayImmediateBreak,
            OnTrayResume,
            OpenSettings,
            OpenAbout,
            ExitApplication);

        WireSchedulerEvents();

        try
        {
            StartupRegistrar.TrySetEnabled(_settings.LaunchAtLogin);
        }
        catch
        {
            // 注册表不可写时不影响运行，设置窗口保存时会再次尝试。
        }

        RefreshTray();

        _tickWatch.Start();
        _lastTickTime = _tickWatch.Elapsed;

        _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _tickTimer.Tick += TickTimer_Tick;
        _tickTimer.Start();

        if (firstRun)
        {
            _trayIcon.ShowInfo(
                "Preserve Your Vision",
                "已在后台运行。默认启用短休息（工作 20 分钟 / 休息 20 秒），可从托盘图标修改设置。");
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // 先放开遮罩的 Closing 拦截，避免注销/关机被遮罩取消。
        _overlayManager?.Hide();
        _notificationService?.CloseAll();

        try
        {
            _statsService?.SaveIfDirty();
        }
        catch
        {
            // 注销/关机时尽可能保存，失败不阻塞系统。
        }

        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tickTimer?.Stop();
        _tickTimer = null;

        try
        {
            _statsService?.SaveIfDirty();
        }
        catch
        {
            // 忽略。
        }

        _overlayManager?.Dispose();
        _overlayManager = null;

        _notificationService?.Dispose();
        _notificationService = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

        _soundService?.Dispose();
        _soundService = null;

        _singleInstance?.Dispose();
        _singleInstance = null;

        base.OnExit(e);
    }

    private void WireSchedulerEvents()
    {
        if (_scheduler is null)
        {
            return;
        }

        _scheduler.StateChanged += (_, _) => RefreshTray();

        _scheduler.PreBreakStarted += (_, args) =>
        {
            var allowActions = !string.Equals(_settings.SkipPolicy, "none", StringComparison.Ordinal);
            _notificationService?.ShowPreBreak(
                args.RemainingSeconds,
                args.Kind,
                allowActions,
                () => _scheduler?.RequestStartBreakNow(),
                () => _scheduler?.RequestSkip(),
                () => _scheduler?.RequestPostpone());
        };

        _scheduler.PreBreakTick += (_, seconds) => _notificationService?.UpdatePreBreak(seconds);

        _scheduler.PreBreakEnded += (_, _) => _notificationService?.HidePreBreak();

        _scheduler.BreakStarted += Scheduler_BreakStarted;
        _scheduler.BreakTick += Scheduler_BreakTick;
        _scheduler.BreakEnded += Scheduler_BreakEnded;

        _scheduler.Snoozed += (_, _) => RefreshTray();
        _scheduler.SnoozeEnded += (_, _) => RefreshTray();

        _scheduler.AutoPauseChanged += (_, _) => RefreshTray();
    }

    private void Scheduler_BreakStarted(object? sender, BreakStartedEventArgs e)
    {
        _notificationService?.HidePreBreak();
        _activeReminderMode = e.ReminderMode;
        _lastBreakMessage = e.Message;

        if (string.Equals(e.ReminderMode, "toast", StringComparison.Ordinal))
        {
            var allowPostpone = !string.Equals(e.SkipPolicy, "none", StringComparison.Ordinal);
            _notificationService?.ShowBreakToast(
                e,
                allowPostpone,
                () => _scheduler?.RequestPostpone());
        }
        else
        {
            _overlayManager?.Show(e);
        }

        _statsService?.SaveIfDirty();
        RefreshTray();
    }

    private void Scheduler_BreakTick(object? sender, BreakTickEventArgs e)
    {
        if (string.Equals(_activeReminderMode, "toast", StringComparison.Ordinal))
        {
            _notificationService?.UpdateBreakToast(e.RemainingSeconds, _lastBreakMessage);
        }
        else
        {
            _overlayManager?.Update(e);
        }
    }

    private void Scheduler_BreakEnded(object? sender, BreakEndedEventArgs e)
    {
        _overlayManager?.Hide();
        _notificationService?.HideBreakToast();
        _activeReminderMode = "";
        _lastBreakMessage = "";
        _statsService?.SaveIfDirty();
        RefreshTray();
    }

    private void OnOverlaySkip()
    {
        _scheduler?.RequestSkip();
        RefreshTray();
    }

    private void OnOverlayPostpone()
    {
        _scheduler?.RequestPostpone();
        RefreshTray();
    }

    private void SnoozeFromTray(TimeSpan? duration)
    {
        var actualDuration = duration ?? SnoozeController.UntilEndOfDay();
        if (_scheduler?.Snooze(actualDuration) == true)
        {
            RefreshTray();
        }
    }

    private void OnTrayImmediateBreak()
    {
        _scheduler?.RequestImmediateBreak();
        RefreshTray();
    }

    private void OnTrayResume()
    {
        _scheduler?.ResumeFromPause();
        RefreshTray();
    }

    private void OpenSettings()
    {
        if (_settingsService is null || _statsService is null || _soundService is null)
        {
            return;
        }

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settings,
            _settingsService,
            _statsService,
            _soundService,
            ApplyNewSettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyNewSettings(Settings settings)
    {
        _settings = settings;

        try
        {
            StartupRegistrar.TrySetEnabled(settings.LaunchAtLogin);
        }
        catch
        {
            // 保存设置已成功，开机自启失败不阻塞。
        }

        _scheduler?.ApplySettings(settings);
        RefreshTray();
    }

    private void OpenAbout()
    {
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void TickTimer_Tick(object? sender, EventArgs e)
    {
        if (_scheduler is null)
        {
            return;
        }

        var now = _tickWatch.Elapsed;
        double deltaSeconds = (now - _lastTickTime).TotalSeconds;
        _lastTickTime = now;

        try
        {
            _scheduler.Tick(deltaSeconds);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("BreakScheduler tick failed: " + ex);
        }

        RefreshTray();

        _tickCount++;
        if (_tickCount % 30 == 0)
        {
            _statsService?.SaveIfDirty();
        }
    }

    private void RefreshTray()
    {
        if (_scheduler is null)
        {
            return;
        }

        try
        {
            _trayIcon?.UpdateStatus(_scheduler.GetStatus());
        }
        catch
        {
            // 托盘更新失败不应导致计时停止。
        }
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _tickTimer?.Stop();

        // 遮罩会拦截 Closing（防止 Alt+F4），退出前必须先显式关闭。
        _overlayManager?.Hide();
        _notificationService?.CloseAll();

        Shutdown();
    }
}
