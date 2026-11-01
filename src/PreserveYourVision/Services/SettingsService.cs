using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using PreserveYourVision.Models;

namespace PreserveYourVision.Services;

/// <summary>
/// 读写 %APPDATA%\PreserveYourVision\settings.json（DESIGN.md §8.3）。
/// 所有配置仅保存在本地。
/// </summary>
public sealed class SettingsService
{
    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PreserveYourVision");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 载入配置。created=true 表示是首次运行（文件不存在），已写入默认配置。
    /// 解析失败时保留损坏文件副本并使用默认值，绝不因配置损坏而崩溃。
    /// </summary>
    public Settings Load(out bool created)
    {
        created = false;

        try
        {
            if (!File.Exists(FilePath))
            {
                created = true;
                var fresh = Settings.CreateDefault();
                Normalize(fresh);
                TrySave(fresh);
                return fresh;
            }

            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
            if (settings is null)
            {
                throw new JsonException("settings.json 反序列化结果为空");
            }

            Normalize(settings);
            return settings;
        }
        catch
        {
            // 配置损坏时备份原文件并回写默认配置，避免每次启动都读到坏文件。
            TryBackupBrokenFile();
            var fallback = Settings.CreateDefault();
            Normalize(fallback);
            TrySave(fallback);
            return fallback;
        }
    }

    /// <summary>原子写入配置（临时文件 + Move 覆盖）。失败时返回 false。</summary>
    public bool TrySave(Settings settings)
    {
        try
        {
            Normalize(settings);
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryBackupBrokenFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var backup = Path.Combine(
                DirectoryPath,
                $"settings.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(FilePath, backup, overwrite: true);
        }
        catch
        {
            // 备份失败不影响启动。
        }
    }

    /// <summary>把任意输入收敛到合法范围，防止手改配置导致崩溃或异常行为。</summary>
    public static void Normalize(Settings settings)
    {
        settings.ShortBreak ??= new ShortBreakSettings();
        settings.LongBreak ??= new LongBreakSettings();
        settings.Overlay ??= new OverlaySettings();
        settings.AutoPause ??= new AutoPauseSettings();

        settings.ShortBreak.IntervalSeconds = Clamp(settings.ShortBreak.IntervalSeconds, 5, 86_400, 1200);
        settings.ShortBreak.DurationSeconds = Clamp(settings.ShortBreak.DurationSeconds, 1, 3600, 20);
        settings.LongBreak.IntervalSeconds = Clamp(settings.LongBreak.IntervalSeconds, 5, 86_400, 3600);
        settings.LongBreak.DurationSeconds = Clamp(settings.LongBreak.DurationSeconds, 1, 3600, 90);

        settings.ReminderMode = settings.ReminderMode?.Trim().ToLowerInvariant() switch
        {
            "toast" => "toast",
            "overlay" => "overlay",
            _ => "overlay"
        };

        settings.PreBreakNoticeSeconds = settings.PreBreakNoticeSeconds switch
        {
            0 => 0,
            5 => 5,
            20 => 20,
            _ => 20
        };

        settings.CustomBreakMessage ??= "";
        settings.SoundFile = string.IsNullOrWhiteSpace(settings.SoundFile)
            ? ""
            : settings.SoundFile.Trim();

        settings.Overlay.CustomImagePath = string.IsNullOrWhiteSpace(settings.Overlay.CustomImagePath)
            ? ""
            : settings.Overlay.CustomImagePath.Trim();
        settings.Overlay.FillMode = settings.Overlay.FillMode?.Trim() switch
        {
            "spanStrip" => "spanStrip",
            "perScreen" => "perScreen",
            _ => "perScreen"
        };
        if (double.IsNaN(settings.Overlay.Opacity) || double.IsInfinity(settings.Overlay.Opacity))
        {
            settings.Overlay.Opacity = 0.9;
        }

        settings.Overlay.Opacity = Math.Clamp(settings.Overlay.Opacity, 0.0, 1.0);
        settings.IdleThresholdSeconds = Clamp(settings.IdleThresholdSeconds, 5, 7200, 60);

        settings.SkipPolicy = settings.SkipPolicy?.Trim().ToLowerInvariant() switch
        {
            "immediate" => "immediate",
            "delay" => "delay",
            "none" => "none",
            _ => "immediate"
        };
        settings.SkipDelaySeconds = Math.Clamp(settings.SkipDelaySeconds, 0, 600);

        settings.Language = settings.Language?.Trim() switch
        {
            "en-US" => "en-US",
            _ => "zh-CN"
        };
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value == 0 && fallback != 0)
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
