using System.Windows;
using System.Windows.Media.Imaging;
using PreserveYourVision.Models;
using Forms = System.Windows.Forms;

namespace PreserveYourVision.UI;

/// <summary>
/// 多显示器遮罩管理（FR-7）：为每个 Screen 创建独立 OverlayWindow，
/// 并把按钮动作回传给调度器。
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly List<OverlayWindow> _windows = new();
    private readonly Action _onSkip;
    private readonly Action _onPostpone;

    public OverlayManager(Action onSkip, Action onPostpone)
    {
        _onSkip = onSkip;
        _onPostpone = onPostpone;
    }

    public bool IsVisible => _windows.Count > 0;

    public void Show(BreakStartedEventArgs args)
    {
        Hide();

        var screens = Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return;
        }

        var virtualBounds = GetVirtualBoundsPhysical(screens);

        // 图片只解码一次，多个显示器窗口共享同一个冻结 BitmapImage，避免重复占用内存。
        var backgroundBitmap = LoadBackgroundBitmap(args.ImagePath, (int)virtualBounds.Width);

        foreach (var screen in screens)
        {
            var window = new OverlayWindow(screen, virtualBounds, args, backgroundBitmap);
            window.SkipRequested += (_, _) => _onSkip();
            window.SnoozeRequested += (_, _) => _onPostpone();
            _windows.Add(window);
        }

        // 先显示副屏，最后显示主屏并抢焦点，避免焦点在多个窗口间跳动。
        foreach (var window in _windows.Where(w => !w.IsPrimaryScreen))
        {
            window.Show();
        }

        foreach (var window in _windows.Where(w => w.IsPrimaryScreen))
        {
            window.Show();
        }
    }

    public void Update(BreakTickEventArgs args)
    {
        foreach (var window in _windows)
        {
            window.UpdateBreak(args);
        }
    }

    public void Hide()
    {
        foreach (var window in _windows)
        {
            window.CloseForced();
        }

        _windows.Clear();
    }

    private static BitmapImage? LoadBackgroundBitmap(string imagePath, int virtualWidthPhysical)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
            // 控制解码尺寸，保证多显示器/高分屏下内存可控。
            bitmap.DecodePixelWidth = Math.Clamp(virtualWidthPhysical, 1280, 4096);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>返回所有显示器并集的物理像素边界（可能为负坐标）。</summary>
    public static Rect GetVirtualBoundsPhysical()
    {
        return GetVirtualBoundsPhysical(Forms.Screen.AllScreens);
    }

    private static Rect GetVirtualBoundsPhysical(Forms.Screen[] screens)
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;

        foreach (var screen in screens)
        {
            left = Math.Min(left, screen.Bounds.Left);
            top = Math.Min(top, screen.Bounds.Top);
            right = Math.Max(right, screen.Bounds.Right);
            bottom = Math.Max(bottom, screen.Bounds.Bottom);
        }

        if (left == int.MaxValue)
        {
            return new Rect(0, 0, 1, 1);
        }

        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public void Dispose()
    {
        Hide();
    }
}
