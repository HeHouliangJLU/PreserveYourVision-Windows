namespace BreaksForEyes.Models;

/// <summary>DESIGN.md §8.1 的五个状态。</summary>
public enum ScheduleState
{
    Working,
    PreBreak,
    Break,
    Snoozed,
    AutoPaused
}

public enum BreakKind
{
    None,
    Short,
    Long
}

public enum BreakEndReason
{
    Completed,
    Skipped,
    Snoozed,
    Interrupted
}

/// <summary>托盘/状态栏展示所需的只读快照。</summary>
public sealed class SchedulerStatus
{
    public ScheduleState State { get; init; }
    public string Text { get; init; } = "";
    public string ToolTip { get; init; } = "";
    public bool CanResume { get; init; }
    public bool CanSnooze { get; init; }
    public bool CanStartBreak { get; init; }
    public bool IsIdle { get; init; }
    public string? AutoPauseReason { get; init; }
}

public sealed class PreBreakStartedEventArgs : EventArgs
{
    public BreakKind Kind { get; init; }
    public int RemainingSeconds { get; init; }
}

public sealed class BreakStartedEventArgs : EventArgs
{
    public BreakKind Kind { get; init; }
    public string ReminderMode { get; init; } = "overlay";
    public int DurationSeconds { get; init; }
    public string Message { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public string FillMode { get; init; } = "perScreen";
    public double ImageOpacity { get; init; } = 0.9;
    public string SkipPolicy { get; init; } = "immediate";
    public int SkipDelaySeconds { get; init; }
}

public sealed class BreakTickEventArgs : EventArgs
{
    public BreakKind Kind { get; init; }
    public int RemainingSeconds { get; init; }
    public int ElapsedSeconds { get; init; }
    public int DurationSeconds { get; init; }
    public bool CanSkip { get; init; }
    public int SkipDelayRemainingSeconds { get; init; }
    public string SkipPolicy { get; init; } = "immediate";
}

public sealed class BreakEndedEventArgs : EventArgs
{
    public BreakKind Kind { get; init; }
    public BreakEndReason Reason { get; init; }
}

public sealed class SnoozedEventArgs : EventArgs
{
    public DateTime Until { get; init; }
}

public sealed class AutoPauseChangedEventArgs : EventArgs
{
    public bool Active { get; init; }
    public string Reason { get; init; } = "";
}
