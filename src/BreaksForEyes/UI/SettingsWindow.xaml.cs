using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BreaksForEyes.Models;
using BreaksForEyes.Services;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace BreaksForEyes.UI;

/// <summary>
/// 设置窗口（DESIGN.md §9.2）。配置在"保存"时统一校验并写入
/// %APPDATA%\BreaksForEyes\settings.json，随后通知调度器即时生效。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly StatsService _statsService;
    private readonly SoundService _soundService;
    private readonly Action<Settings> _onSaved;

    public SettingsWindow(
        Settings settings,
        SettingsService settingsService,
        StatsService statsService,
        SoundService soundService,
        Action<Settings> onSaved)
    {
        _settingsService = settingsService;
        _statsService = statsService;
        _soundService = soundService;
        _onSaved = onSaved;

        InitializeComponent();
        LoadSettings(settings);

        OpacitySlider.ValueChanged += (_, _) => UpdateOpacityLabel();
        SkipPolicyCombo.SelectionChanged += (_, _) => UpdateDependentControls();
        ShortBreakEnabledCheck.Checked += (_, _) => UpdateDependentControls();
        ShortBreakEnabledCheck.Unchecked += (_, _) => UpdateDependentControls();
        LongBreakEnabledCheck.Checked += (_, _) => UpdateDependentControls();
        LongBreakEnabledCheck.Unchecked += (_, _) => UpdateDependentControls();
        SoundEnabledCheck.Checked += (_, _) => UpdateDependentControls();
        SoundEnabledCheck.Unchecked += (_, _) => UpdateDependentControls();

        UpdateDependentControls();
        RefreshStats();
    }

    private void LoadSettings(Settings settings)
    {
        ShortBreakEnabledCheck.IsChecked = settings.ShortBreak.Enabled;
        ShortIntervalBox.Text = FormatMinutes(settings.ShortBreak.IntervalSeconds);
        ShortDurationBox.Text = settings.ShortBreak.DurationSeconds.ToString(CultureInfo.InvariantCulture);

        LongBreakEnabledCheck.IsChecked = settings.LongBreak.Enabled;
        LongIntervalBox.Text = FormatMinutes(settings.LongBreak.IntervalSeconds);
        LongDurationBox.Text = settings.LongBreak.DurationSeconds.ToString(CultureInfo.InvariantCulture);

        SelectByTag(ReminderModeCombo, settings.ReminderMode);
        SelectByTag(PreBreakCombo, settings.PreBreakNoticeSeconds.ToString(CultureInfo.InvariantCulture));

        ImagePathBox.Text = settings.Overlay.CustomImagePath;
        SelectByTag(FillModeCombo, settings.Overlay.FillMode);
        OpacitySlider.Value = Math.Clamp(settings.Overlay.Opacity, 0.0, 1.0);
        UpdateOpacityLabel();

        SelectByTag(SkipPolicyCombo, settings.SkipPolicy);
        SkipDelayBox.Text = settings.SkipDelaySeconds.ToString(CultureInfo.InvariantCulture);

        AutoFullscreenCheck.IsChecked = settings.AutoPause.Fullscreen;
        AutoMeetingCheck.IsChecked = settings.AutoPause.Meeting;
        AutoVideoCheck.IsChecked = settings.AutoPause.Video;

        LaunchAtLoginCheck.IsChecked = settings.LaunchAtLogin;
        IdleThresholdBox.Text = settings.IdleThresholdSeconds.ToString(CultureInfo.InvariantCulture);
        SelectByTag(LanguageCombo, settings.Language);

        CustomMessageBox.Text = settings.CustomBreakMessage;
        SoundEnabledCheck.IsChecked = settings.SoundEnabled;
        SoundPathBox.Text = settings.SoundFile;
    }

    private void UpdateOpacityLabel()
    {
        OpacityValueText.Text = $"{Math.Round(OpacitySlider.Value * 100)}%";
    }

    private void UpdateDependentControls()
    {
        bool shortEnabled = ShortBreakEnabledCheck.IsChecked == true;
        ShortIntervalBox.IsEnabled = shortEnabled;
        ShortDurationBox.IsEnabled = shortEnabled;

        bool longEnabled = LongBreakEnabledCheck.IsChecked == true;
        LongIntervalBox.IsEnabled = longEnabled;
        LongDurationBox.IsEnabled = longEnabled;

        bool delayPolicy = string.Equals(SelectedTag(SkipPolicyCombo), "delay", StringComparison.Ordinal);
        SkipDelayBox.IsEnabled = delayPolicy;

        bool soundEnabled = SoundEnabledCheck.IsChecked == true;
        SoundPathBox.IsEnabled = soundEnabled;
    }

    private void RefreshStats()
    {
        StatsTextBlock.Text = _statsService.GetSummary();
    }

    private void BrowseImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择遮罩背景图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            ImagePathBox.Text = dialog.FileName;
        }
    }

    private void ClearImageButton_Click(object sender, RoutedEventArgs e)
    {
        ImagePathBox.Text = "";
    }

    private void BrowseSoundButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择提示音文件",
            Filter = "音频文件|*.wav;*.mp3;*.wma;*.m4a;*.aac|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            SoundPathBox.Text = dialog.FileName;
        }
    }

    private void ClearSoundButton_Click(object sender, RoutedEventArgs e)
    {
        SoundPathBox.Text = "";
    }

    private void TestSoundButton_Click(object sender, RoutedEventArgs e)
    {
        _soundService.PlayPreview(SoundPathBox.Text.Trim());
    }

    private void RefreshStatsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshStats();
    }

    private void ClearStatsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "确定要清空全部本地统计（含历史记录）吗？此操作不可撤销。",
            "Breaks for Eyes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _statsService.ClearAll();
            RefreshStats();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadIntervalMinutes(ShortIntervalBox, "短休息工作间隔", out int shortIntervalSeconds) ||
            !TryReadInt(ShortDurationBox, 1, 3600, "短休息时长（秒）", out int shortDuration) ||
            !TryReadIntervalMinutes(LongIntervalBox, "长休息工作间隔", out int longIntervalSeconds) ||
            !TryReadInt(LongDurationBox, 1, 3600, "长休息时长（秒）", out int longDuration) ||
            !TryReadInt(SkipDelayBox, 0, 600, "跳过延迟（秒）", out int skipDelay) ||
            !TryReadInt(IdleThresholdBox, 5, 7200, "空闲检测阈值（秒）", out int idleThreshold))
        {
            return;
        }

        string imagePath = ImagePathBox.Text.Trim();
        if (imagePath.Length > 0 && !File.Exists(imagePath))
        {
            var result = MessageBox.Show(
                this,
                "图片文件不存在，将回退为默认背景。仍要保存吗？",
                "Breaks for Eyes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var preBreakSeconds = int.TryParse(
            SelectedTag(PreBreakCombo),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsedPreBreak)
            ? parsedPreBreak
            : 20;

        var settings = new Settings
        {
            ShortBreak = new ShortBreakSettings
            {
                Enabled = ShortBreakEnabledCheck.IsChecked == true,
                IntervalSeconds = shortIntervalSeconds,
                DurationSeconds = shortDuration
            },
            LongBreak = new LongBreakSettings
            {
                Enabled = LongBreakEnabledCheck.IsChecked == true,
                IntervalSeconds = longIntervalSeconds,
                DurationSeconds = longDuration
            },
            ReminderMode = SelectedTag(ReminderModeCombo),
            PreBreakNoticeSeconds = preBreakSeconds,
            CustomBreakMessage = CustomMessageBox.Text.Trim(),
            SoundEnabled = SoundEnabledCheck.IsChecked == true,
            SoundFile = SoundPathBox.Text.Trim(),
            Overlay = new OverlaySettings
            {
                CustomImagePath = imagePath,
                FillMode = SelectedTag(FillModeCombo),
                Opacity = Math.Round(OpacitySlider.Value, 2)
            },
            IdleThresholdSeconds = idleThreshold,
            AutoPause = new AutoPauseSettings
            {
                Fullscreen = AutoFullscreenCheck.IsChecked == true,
                Meeting = AutoMeetingCheck.IsChecked == true,
                Video = AutoVideoCheck.IsChecked == true
            },
            SkipPolicy = SelectedTag(SkipPolicyCombo),
            SkipDelaySeconds = skipDelay,
            LaunchAtLogin = LaunchAtLoginCheck.IsChecked == true,
            Language = SelectedTag(LanguageCombo)
        };

        SettingsService.Normalize(settings);

        if (!_settingsService.TrySave(settings))
        {
            MessageBox.Show(
                this,
                "保存设置失败，请检查 %APPDATA%\\BreaksForEyes 目录是否可写。",
                "Breaks for Eyes",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _onSaved(settings);
        Close();
    }

    private bool TryReadIntervalMinutes(TextBox box, string label, out int seconds)
    {
        var text = box.Text.Trim();
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double minutes) ||
                      double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes);

        seconds = parsed && double.IsFinite(minutes)
            ? (int)Math.Round(minutes * 60, MidpointRounding.AwayFromZero)
            : 0;

        if (!parsed || seconds < 5 || seconds > 86_400)
        {
            MessageBox.Show(
                this,
                $"{label}必须以分钟填写，范围 0.083（5 秒）到 1440（24 小时）。",
                "输入无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            box.Focus();
            box.SelectAll();
            return false;
        }

        return true;
    }

    private static string FormatMinutes(int seconds)
    {
        return (seconds / 60.0).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private bool TryReadInt(TextBox box, int min, int max, string label, out int value)
    {
        var text = box.Text.Trim();
        bool parsed = int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) ||
                      int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        if (!parsed || value < min || value > max)
        {
            MessageBox.Show(
                this,
                $"{label}必须是 {min} 到 {max} 之间的整数。",
                "输入无效",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            box.Focus();
            box.SelectAll();
            return false;
        }

        return true;
    }

    private static void SelectByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                string.Equals(comboItem.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
    }
}
