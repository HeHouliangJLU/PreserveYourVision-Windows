namespace PreserveYourVision.Models;

/// <summary>每日一条的历史记录，对应 DESIGN.md §8.3 stats.json 的 history 元素。</summary>
public sealed class DailyStats
{
    public string Date { get; set; } = "";
    public long ActiveSeconds { get; set; }
    public int Completed { get; set; }

    public override string ToString() => $"{Date}  {FormatDuration(ActiveSeconds)}  已休 {Completed} 次";

    private static string FormatDuration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分";
        }

        return $"{ts.Minutes} 分 {ts.Seconds} 秒";
    }
}

/// <summary>
/// 本地统计（FR-12）。对应 DESIGN.md §8.3 的 stats.json。
/// 额外增加 date 字段用于跨天归档（设计文档未明示跨天方式）。
/// </summary>
public sealed class StatsData
{
    public string Date { get; set; } = "";
    public long ActiveSecondsToday { get; set; }
    public int ExpectedBreaks { get; set; }
    public int CompletedBreaks { get; set; }
    public int SkippedBreaks { get; set; }
    public int SnoozedBreaks { get; set; }
    public List<DailyStats> History { get; set; } = new();
}
