using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BreaksForEyes.Models;

namespace BreaksForEyes.Services;

/// <summary>
/// 本地统计（FR-12）。数据仅写入 %APPDATA%\BreaksForEyes\stats.json。
/// 写盘只在脏标记存在时进行（由 App 每 30 秒调用一次，退出时再保存一次）。
/// </summary>
public sealed class StatsService
{
    public static string DirectoryPath => SettingsService.DirectoryPath;
    public static string FilePath => Path.Combine(DirectoryPath, "stats.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private StatsData _data = new();
    private bool _dirty;
    private double _activeCarry;

    public StatsData Data => _data;

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _data = JsonSerializer.Deserialize<StatsData>(json, JsonOptions) ?? new StatsData();
            }
        }
        catch
        {
            _data = new StatsData();
        }

        _data.History ??= new List<DailyStats>();
        _data.ActiveSecondsToday = Math.Max(0, _data.ActiveSecondsToday);
        _data.ExpectedBreaks = Math.Max(0, _data.ExpectedBreaks);
        _data.CompletedBreaks = Math.Max(0, _data.CompletedBreaks);
        _data.SkippedBreaks = Math.Max(0, _data.SkippedBreaks);
        _data.SnoozedBreaks = Math.Max(0, _data.SnoozedBreaks);

        EnsureToday();
    }

    /// <summary>跨天时把上一日数据压入 history 并清零当日计数。</summary>
    public void EnsureToday()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (string.Equals(_data.Date, today, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_data.Date) &&
            (_data.ActiveSecondsToday > 0 || _data.CompletedBreaks > 0 ||
             _data.ExpectedBreaks > 0 || _data.SkippedBreaks > 0 || _data.SnoozedBreaks > 0))
        {
            _data.History.Add(new DailyStats
            {
                Date = _data.Date,
                ActiveSeconds = _data.ActiveSecondsToday,
                Completed = _data.CompletedBreaks
            });

            // 只保留最近一年，避免文件无限增长。
            if (_data.History.Count > 366)
            {
                _data.History.RemoveRange(0, _data.History.Count - 366);
            }
        }

        _data.Date = today;
        _data.ActiveSecondsToday = 0;
        _data.ExpectedBreaks = 0;
        _data.CompletedBreaks = 0;
        _data.SkippedBreaks = 0;
        _data.SnoozedBreaks = 0;
        _activeCarry = 0;
        _dirty = true;
    }

    public void AddActiveSeconds(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        EnsureToday();
        _activeCarry += seconds;
        var whole = (long)_activeCarry;
        if (whole <= 0)
        {
            return;
        }

        _activeCarry -= whole;
        _data.ActiveSecondsToday += whole;
        _dirty = true;
    }

    public void IncrementExpected()
    {
        EnsureToday();
        _data.ExpectedBreaks++;
        _dirty = true;
    }

    public void IncrementCompleted()
    {
        EnsureToday();
        _data.CompletedBreaks++;
        _dirty = true;
    }

    public void IncrementSkipped()
    {
        EnsureToday();
        _data.SkippedBreaks++;
        _dirty = true;
    }

    public void IncrementSnoozed()
    {
        EnsureToday();
        _data.SnoozedBreaks++;
        _dirty = true;
    }

    public void ClearAll()
    {
        _data = new StatsData { Date = DateTime.Today.ToString("yyyy-MM-dd") };
        _activeCarry = 0;
        _dirty = true;
        SaveIfDirty();
    }

    public void SaveIfDirty()
    {
        if (!_dirty)
        {
            return;
        }

        try
        {
            EnsureToday();
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(_data, JsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, FilePath, overwrite: true);
            _dirty = false;
        }
        catch
        {
            // 保持脏标记，下一次再尝试。
        }
    }

    public string GetSummary()
    {
        EnsureToday();
        var sb = new StringBuilder();
        sb.AppendLine($"今日活跃时长：{FormatDuration(_data.ActiveSecondsToday)}");
        sb.AppendLine(
            $"应休 {_data.ExpectedBreaks} 次 · 已休 {_data.CompletedBreaks} 次 · " +
            $"跳过 {_data.SkippedBreaks} 次 · 贪睡 {_data.SnoozedBreaks} 次");

        if (_data.History.Count == 0)
        {
            sb.AppendLine("历史记录：暂无");
        }
        else
        {
            sb.AppendLine($"历史记录：共 {_data.History.Count} 天");
            foreach (var day in _data.History.TakeLast(7).Reverse())
            {
                sb.AppendLine($"  {day}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatDuration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes} 分 {ts.Seconds} 秒";
        }

        return $"{ts.Seconds} 秒";
    }
}
