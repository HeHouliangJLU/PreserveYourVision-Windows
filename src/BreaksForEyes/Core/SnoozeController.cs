namespace BreaksForEyes.Core;

/// <summary>
/// 贪睡/暂停计时（FR-5）。仅记录截止时间，状态机由 BreakScheduler 驱动。
/// </summary>
public sealed class SnoozeController
{
    public DateTime? Until { get; private set; }

    public bool IsActive => Until.HasValue && DateTime.Now < Until.Value;

    public double RemainingSeconds
    {
        get
        {
            if (!Until.HasValue)
            {
                return 0;
            }

            return Math.Max(0, (Until.Value - DateTime.Now).TotalSeconds);
        }
    }

    public void Begin(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(1);
        }

        var until = DateTime.Now + duration;
        if (!Until.HasValue || until > Until.Value)
        {
            Until = until;
        }
    }

    public void End()
    {
        Until = null;
    }

    /// <summary>"当天"选项：暂停到当天 23:59:59；若已接近午夜则至少暂停 1 分钟。</summary>
    public static TimeSpan UntilEndOfDay()
    {
        var remaining = DateTime.Today.AddDays(1) - DateTime.Now;
        return remaining < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : remaining;
    }
}
