using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BreaksForEyes.Core;
using BreaksForEyes.Models;
using Forms = System.Windows.Forms;

namespace BreaksForEyes.UI;

/// <summary>
/// 单个显示器的全屏遮罩（FR-7）。每个 Screen 一个独立窗口，
/// 用 SetWindowPos 按物理像素铺满屏幕，避免混合 DPI 下的覆盖漏洞。
/// 层次：图片层（可调透明度）→ 半透明深色遮罩 → 不透明文案与按钮（FR-15）。
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Rect _screenBoundsPhysical;
    private readonly Rect _virtualBoundsPhysical;
    private readonly bool _isPrimaryScreen;
    private readonly int _durationSeconds;
    private string _skipPolicy;
    private int _skipDelaySeconds;
    private int _duration;
    private bool _canSkip;
    private bool _allowClose;
    private Image? _backgroundImage;
    private string _fillMode = "perScreen";
    private double _imageOpacity;

    public event EventHandler? SkipRequested;
    public event EventHandler? SnoozeRequested;

    public bool IsPrimaryScreen => _isPrimaryScreen;

    public OverlayWindow(
        Forms.Screen screen,
        Rect virtualBoundsPhysical,
        BreakStartedEventArgs args,
        BitmapImage? backgroundBitmap)
    {
        InitializeComponent();

        _screenBoundsPhysical = new Rect(
            screen.Bounds.X,
            screen.Bounds.Y,
            screen.Bounds.Width,
            screen.Bounds.Height);
        _virtualBoundsPhysical = virtualBoundsPhysical;
        _isPrimaryScreen = screen.Primary;
        ShowActivated = _isPrimaryScreen;

        _skipPolicy = args.SkipPolicy;
        _skipDelaySeconds = Math.Max(0, args.SkipDelaySeconds);
        _durationSeconds = Math.Max(1, args.DurationSeconds);
        _duration = _durationSeconds;

        Title = args.Kind == BreakKind.Long ? "Breaks for Eyes · 长休息" : "Breaks for Eyes · 休息";
        TitleText.Text = args.Kind == BreakKind.Long ? "起身活动一下" : "休息一下";
        MessageText.Text = args.Message;

        BreakProgress.Minimum = 0;
        BreakProgress.Maximum = _durationSeconds;
        CountdownText.Text = BreakScheduler.FormatDuration(_durationSeconds);

        ApplyBackground(backgroundBitmap, args.FillMode, args.ImageOpacity);
        ApplyBreakState(new BreakTickEventArgs
        {
            Kind = args.Kind,
            RemainingSeconds = _durationSeconds,
            ElapsedSeconds = 0,
            DurationSeconds = _durationSeconds,
            CanSkip = ComputeInitialCanSkip(),
            SkipDelayRemainingSeconds = Math.Max(0, _skipDelaySeconds),
            SkipPolicy = _skipPolicy
        });

        SourceInitialized += OverlayWindow_SourceInitialized;
        Loaded += OverlayWindow_Loaded;
        SizeChanged += (_, _) => LayoutBackground();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ApplyScreenBounds, DispatcherPriority.Loaded);
        Closing += OverlayWindow_Closing;
    }

    public void UpdateBreak(BreakTickEventArgs args)
    {
        ApplyBreakState(args);
    }

    /// <summary>由 OverlayManager 在调度器结束休息时调用，允许真正关闭窗口。</summary>
    public void CloseForced()
    {
        _allowClose = true;
        try
        {
            Close();
        }
        catch
        {
            // 窗口可能已经关闭。
        }
    }

    private bool ComputeInitialCanSkip()
    {
        return _skipPolicy switch
        {
            "immediate" => true,
            "delay" => _skipDelaySeconds <= 0,
            _ => false
        };
    }

    private void ApplyBreakState(BreakTickEventArgs args)
    {
        _duration = Math.Max(1, args.DurationSeconds);
        _canSkip = args.CanSkip;

        CountdownText.Text = BreakScheduler.FormatDuration(args.RemainingSeconds);
        BreakProgress.Maximum = _duration;
        BreakProgress.Value = Math.Clamp(_duration - args.RemainingSeconds, 0, _duration);

        if (string.Equals(args.SkipPolicy, "none", StringComparison.Ordinal))
        {
            SkipButton.Visibility = Visibility.Collapsed;
            SnoozeButton.Visibility = Visibility.Collapsed;
            HintText.Text = "本次休息不可跳过，请让眼睛暂时离开屏幕";
            return;
        }

        SkipButton.Visibility = Visibility.Visible;
        SnoozeButton.Visibility = Visibility.Visible;
        SkipButton.IsEnabled = args.CanSkip;
        SnoozeButton.IsEnabled = args.CanSkip;

        if (args.CanSkip)
        {
            SkipButton.Content = "跳过休息";
            SnoozeButton.Content = "推迟 5 分钟";
            HintText.Text = "Esc 跳过 · Tab 切换按钮 · Enter 确认";
        }
        else
        {
            SkipButton.Content = $"跳过休息（{Math.Max(0, args.SkipDelayRemainingSeconds)}s）";
            SnoozeButton.Content = $"推迟（{Math.Max(0, args.SkipDelayRemainingSeconds)}s）";
            HintText.Text = "请稍等片刻，再决定是否跳过";
        }
    }

    private void ApplyBackground(BitmapImage? bitmap, string fillMode, double opacity)
    {
        _fillMode = string.Equals(fillMode, "spanStrip", StringComparison.Ordinal)
            ? "spanStrip"
            : "perScreen";
        _imageOpacity = Math.Clamp(opacity, 0.0, 1.0);

        ImageCanvas.Children.Clear();
        ImageCanvas.Visibility = Visibility.Collapsed;
        Scrim.Opacity = 0;
        _backgroundImage = null;

        if (bitmap is null)
        {
            // 未设置（或无法读取）自定义图片时使用默认渐变背景。
            var gradient = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.34),
                GradientOrigin = new Point(0.5, 0.34),
                RadiusX = 0.85,
                RadiusY = 0.85
            };
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x40, 0x50), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x1A, 0x23), 0.62));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0x06, 0x0C, 0x11), 1));
            Root.Background = gradient;
            return;
        }

        Root.Background = new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x0C));
        Scrim.Opacity = 1.0;

        _backgroundImage = new Image
        {
            Source = bitmap,
            Stretch = Stretch.UniformToFill,
            Opacity = _imageOpacity,
            SnapsToDevicePixels = true
        };

        ImageCanvas.Children.Add(_backgroundImage);
        ImageCanvas.Visibility = Visibility.Visible;
        LayoutBackground();
    }

    private void LayoutBackground()
    {
        if (_backgroundImage is null)
        {
            return;
        }

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_fillMode == "spanStrip" &&
            _screenBoundsPhysical.Width > 0 &&
            _virtualBoundsPhysical.Width > 0)
        {
            // 按虚拟桌面宽度缩放原图，再按当前屏幕在虚拟桌面中的横向偏移裁剪，
            // 形成一条横跨所有显示器的连续长条横幅。
            double scale = width / _screenBoundsPhysical.Width;
            _backgroundImage.Width = _virtualBoundsPhysical.Width * scale;
            _backgroundImage.Height = height;
            Canvas.SetLeft(
                _backgroundImage,
                -(_screenBoundsPhysical.X - _virtualBoundsPhysical.X) * scale);
            Canvas.SetTop(_backgroundImage, 0);
        }
        else
        {
            // 每屏同图，各自适配该屏尺寸。
            _backgroundImage.Width = width;
            _backgroundImage.Height = height;
            Canvas.SetLeft(_backgroundImage, 0);
            Canvas.SetTop(_backgroundImage, 0);
        }
    }

    private void OverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int exStyle = GetWindowLongW(handle, GWL_EXSTYLE);
        SetWindowLongW(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        ApplyScreenBounds();
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyScreenBounds();
        LayoutBackground();

        if (_isPrimaryScreen)
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
        }
    }

    private void ApplyScreenBounds()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(
                handle,
                HwndTopmost,
                (int)Math.Round(_screenBoundsPhysical.X),
                (int)Math.Round(_screenBoundsPhysical.Y),
                Math.Max(1, (int)Math.Round(_screenBoundsPhysical.Width)),
                Math.Max(1, (int)Math.Round(_screenBoundsPhysical.Height)),
                SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
        }
        catch
        {
            // 窗口销毁过程中可能失败，忽略。
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canSkip)
        {
            return;
        }

        SkipRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_canSkip)
        {
            return;
        }

        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OverlayWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_canSkip && SkipButton.Visibility == Visibility.Visible)
            {
                SkipRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OverlayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // 遮罩不允许被 Alt+F4 等普通方式关闭；只能由调度器结束休息时关闭。
            e.Cancel = true;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);
}
