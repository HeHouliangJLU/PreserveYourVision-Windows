using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PreserveYourVision.UI;

/// <summary>
/// 轻量自定义通知窗口（FR-2 的 toast 模式 / FR-3 的提前预警）。
/// 不使用 WinRT Toast，避免 AUMID 注册与额外 NuGet 依赖；不抢占焦点、不阻断操作。
/// </summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    private int _autoCloseRemaining;

    public bool IsClosed { get; private set; }

    public ToastWindow()
    {
        InitializeComponent();

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _autoCloseTimer.Tick += AutoCloseTimer_Tick;

        Loaded += ToastWindow_Loaded;
        Closing += ToastWindow_Closing;
    }

    public void ShowToast(
        string title,
        string message,
        int autoCloseSeconds,
        params (string Label, Action Callback, bool Secondary)[] buttons)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ButtonPanel.Children.Clear();

        foreach (var (label, callback, secondary) in buttons)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource(secondary ? "ToastSecondaryButtonStyle" : "ToastButtonStyle")
            };

            var capturedAction = callback;
            button.Click += (_, _) =>
            {
                CloseNotification();
                try
                {
                    capturedAction();
                }
                catch
                {
                    // 通知按钮回调失败不应导致应用崩溃。
                }
            };

            ButtonPanel.Children.Add(button);
        }

        Show();
        UpdateLayout();
        PositionWindow();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        if (autoCloseSeconds > 0)
        {
            _autoCloseRemaining = autoCloseSeconds;
            _autoCloseTimer.Start();
        }
    }

    public void UpdateMessage(string message)
    {
        if (!IsClosed)
        {
            MessageText.Text = message;
        }
    }

    public void CloseNotification()
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        _autoCloseTimer.Stop();

        try
        {
            Close();
        }
        catch
        {
            // 窗口可能已经关闭。
        }
    }

    private void ToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLayout();
        PositionWindow();
    }

    private void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 16;
        Top = workArea.Bottom - ActualHeight - 16;
    }

    private void AutoCloseTimer_Tick(object? sender, EventArgs e)
    {
        _autoCloseRemaining--;
        if (_autoCloseRemaining <= 0)
        {
            CloseNotification();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseNotification();
    }

    private void ToastWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        IsClosed = true;
        _autoCloseTimer.Stop();
    }
}
