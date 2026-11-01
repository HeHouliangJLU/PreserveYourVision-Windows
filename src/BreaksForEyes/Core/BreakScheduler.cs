using BreaksForEyes.Models;
using BreaksForEyes.Services;

namespace BreaksForEyes.Core;

/// <summary>
/// 调度核心（DESIGN.md §8.1）。短休息、长休息两个独立计时器共用同一状态机：
/// Working → PreBreak → Break → Snoozed / AutoPaused。
/// 仅活跃时间累计；两者几乎同时到期时长休息优先；短休息被吸收并重置，不计为跳过。
/// </summary>
public sealed class BreakScheduler
{
    private const double MaxTickSeconds = 5.0;
    private const double LongBreakPriorityWindowSeconds = 5.0;

    private readonly StatsService _stats;
    private readonly IdleDetector _idleDetector;
    private readonly AutoPauseDetector _autoPauseDetector;
    private readonly SoundService _soundService;
    private readonly SnoozeController _snooze = new();

    private Settings _settings;

    private ScheduleState _state = ScheduleState.Working;
    private double _shortElapsed;
    private double _longElapsed;

    private double _preBreakRemaining;
    private BreakKind _pendingKind = BreakKind.None;

    private BreakKind _breakKind = BreakKind.None;
    private double _breakRemaining;
    private double _breakElapsed;
    private int _breakDuration;
    private string _breakSkipPolicy = "immediate";
    private int _breakSkipDelaySeconds;

    private double _idleSeconds;

    private bool _autoPauseOverridden;
    private string _autoPauseReason = "";

    private bool _snoozeStartBreakAfter;
    private bool _snoozeCameFromBreak;
    private BreakKind _snoozePendingKind = BreakKind.None;

    public BreakScheduler(
        Settings settings,
        StatsService stats,
        IdleDetector? idleDetector = null,
        AutoPauseDetector? autoPauseDetector = null,
        SoundService? soundService = null)
    {
        _settings = settings;
        _stats = stats;
        _idleDetector = idleDetector ?? new IdleDetector();
        _autoPauseDetector = autoPauseDetector ?? new AutoPauseDetector();
        _soundService = soundService ?? new SoundService();
    }

    public event EventHandler<ScheduleState>? StateChanged;
    public event EventHandler<PreBreakStartedEventArgs>? PreBreakStarted;
    public event EventHandler<int>? PreBreakTick;
    public event EventHandler? PreBreakEnded;
    public event EventHandler<BreakStartedEventArgs>? BreakStarted;
    public event EventHandler<BreakTickEventArgs>? BreakTick;
    public event EventHandler<BreakEndedEventArgs>? BreakEnded;
    public event EventHandler<SnoozedEventArgs>? Snoozed;
    public event EventHandler? SnoozeEnded;
    public event EventHandler<AutoPauseChangedEventArgs>? AutoPauseChanged;

    public ScheduleState State => _state;

    public bool CanSkipNow => _breakSkipPolicy switch
    {
        "immediate" => true,
        "delay" => _breakElapsed >= Math.Max(0, _breakSkipDelaySeconds),
        _ => false
    };

    public double ShortElapsedSeconds => _shortElapsed;
    public double LongElapsedSeconds => _longElapsed;
    public double IdleSeconds => _idleSeconds;

    /// <summary>每秒由 UI 线程调用一次。deltaSeconds 使用真实经过时间，限制上限避免休眠唤醒后瞬间完成休息。</summary>
    public void Tick(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return;
        }

        deltaSeconds = Math.Min(deltaSeconds, MaxTickSeconds);

        switch (_state)
        {
            case ScheduleState.Snoozed:
                TickSnoozed();
                break;
            case ScheduleState.AutoPaused:
                TickAutoPaused();
                break;
            case ScheduleState.PreBreak:
                TickPreBreak(deltaSeconds);
                break;
            case ScheduleState.Break:
                TickBreak(deltaSeconds);
                break;
            case ScheduleState.Working:
            default:
                TickWorking(deltaSeconds);
                break;
        }
    }

    public void ApplySettings(Settings settings)
    {
        _settings = settings;

        if (_state == ScheduleState.PreBreak && IsBreakDisabled(_pendingKind))
        {
            CancelPreBreak();
        }

        if (_state == ScheduleState.Break && IsBreakDisabled(_breakKind))
        {
            EndBreak(BreakEndReason.Interrupted);
        }
    }

    /// <summary>托盘"立即休息"。可在 Working / Snoozed / AutoPaused 状态下使用。</summary>
    public bool RequestImmediateBreak()
    {
        if (_state is ScheduleState.PreBreak or ScheduleState.Break)
        {
            return false;
        }

        if (_state == ScheduleState.Snoozed)
        {
            _snooze.End();
            ResetSnoozeFlags();
        }

        _autoPauseOverridden = false;
        _autoPauseReason = "";

        var kind = _settings.ShortBreak.Enabled
            ? BreakKind.Short
            : _settings.LongBreak.Enabled
                ? BreakKind.Long
                : BreakKind.Short;

        // 手动"立即休息"优先于总开关：即使两类提醒都关闭，也允许用户主动休息一次。
        BeginBreak(kind, force: true);
        return true;
    }

    /// <summary>跳过当前预警或休息（受 FR-11 跳过策略约束）。</summary>
    public bool RequestSkip()
    {
        if (_state == ScheduleState.PreBreak)
        {
            if (string.Equals(_settings.SkipPolicy, "none", StringComparison.Ordinal))
            {
                return false;
            }

            var kind = _pendingKind;
            _pendingKind = BreakKind.None;
            _preBreakRemaining = 0;
            ResetTimer(kind);
            _stats.IncrementSkipped();
            SetState(ScheduleState.Working);
            PreBreakEnded?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (_state == ScheduleState.Break)
        {
            if (!CanSkipNow)
            {
                return false;
            }

            EndBreak(BreakEndReason.Skipped);
            return true;
        }

        return false;
    }

    /// <summary>预休息阶段点击"立即休息"：跳过剩余预警倒计时，立即进入休息。</summary>
    public bool RequestStartBreakNow()
    {
        if (_state != ScheduleState.PreBreak)
        {
            return false;
        }

        var kind = _pendingKind;
        _pendingKind = BreakKind.None;
        _preBreakRemaining = 0;
        PreBreakEnded?.Invoke(this, EventArgs.Empty);

        if (kind != BreakKind.None)
        {
            BeginBreak(kind);
        }
        else
        {
            SetState(ScheduleState.Working);
        }

        return true;
    }

    /// <summary>预休息"推迟 5 分钟"或遮罩"推迟 5 分钟"。</summary>
    public bool RequestPostpone()
    {
        if (_state is not (ScheduleState.PreBreak or ScheduleState.Break))
        {
            return false;
        }

        return Snooze(TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// 贪睡/暂停（FR-5）。Working 状态下冻结计时；PreBreak 状态推迟 5 分钟后再开始休息；
    /// Break 状态下相当于"延迟本次休息"，并重置本轮计时。
    /// </summary>
    public bool Snooze(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return false;
        }

        if (_state == ScheduleState.Break && !CanSkipNow)
        {
            return false;
        }

        if (_state is ScheduleState.PreBreak or ScheduleState.Break)
        {
            if (string.Equals(_settings.SkipPolicy, "none", StringComparison.Ordinal))
            {
                return false;
            }
        }

        bool alreadySnoozed = _state == ScheduleState.Snoozed;

        switch (_state)
        {
            case ScheduleState.Break:
            {
                // 先捕获当前休息类型，EndBreak 会把 _breakKind 清空。
                var snoozedBreakKind = _breakKind;
                _snoozeCameFromBreak = true;
                // 与 PreBreak 分支保持同一语义：贪睡到期后重新弹出同类型休息。
                _snoozeStartBreakAfter = true;
                _snoozePendingKind = snoozedBreakKind;
                EndBreak(BreakEndReason.Snoozed);
                // 本次休息被推迟：下一轮从头开始计时。
                _shortElapsed = 0;
                _longElapsed = 0;
                break;
            }

            case ScheduleState.PreBreak:
                _snoozeStartBreakAfter = true;
                _snoozeCameFromBreak = false;
                _snoozePendingKind = _pendingKind;
                _pendingKind = BreakKind.None;
                _preBreakRemaining = 0;
                PreBreakEnded?.Invoke(this, EventArgs.Empty);
                break;

            case ScheduleState.AutoPaused:
                _autoPauseOverridden = false;
                _autoPauseReason = "";
                break;

            case ScheduleState.Working:
            case ScheduleState.Snoozed:
            default:
                break;
        }

        _snooze.Begin(duration);

        if (!alreadySnoozed)
        {
            _stats.IncrementSnoozed();
        }

        SetState(ScheduleState.Snoozed);
        Snoozed?.Invoke(this, new SnoozedEventArgs { Until = _snooze.Until ?? DateTime.Now });
        return true;
    }

    /// <summary>托盘"恢复计时"。Snoozed 恢复计时；AutoPaused 手动覆盖自动暂停。</summary>
    public bool ResumeFromPause()
    {
        if (_state == ScheduleState.Snoozed)
        {
            _snooze.End();
            ResetSnoozeFlags();
            SetState(ScheduleState.Working);
            SnoozeEnded?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (_state == ScheduleState.AutoPaused)
        {
            _autoPauseOverridden = true;
            _autoPauseReason = "";
            SetState(ScheduleState.Working);
            AutoPauseChanged?.Invoke(this, new AutoPauseChangedEventArgs { Active = false, Reason = "手动恢复" });
            return true;
        }

        return false;
    }

    public SchedulerStatus GetStatus()
    {
        switch (_state)
        {
            case ScheduleState.PreBreak:
            {
                var text = "休息即将开始 " + FormatDuration(_preBreakRemaining);
                return new SchedulerStatus
                {
                    State = _state,
                    Text = text,
                    ToolTip = text,
                    CanResume = false,
                    CanSnooze = !string.Equals(_settings.SkipPolicy, "none", StringComparison.Ordinal),
                    CanStartBreak = false,
                    IsIdle = false
                };
            }

            case ScheduleState.Break:
            {
                var title = _breakKind == BreakKind.Long ? "长休息" : "休息中";
                var text = title + " " + FormatDuration(_breakRemaining);
                return new SchedulerStatus
                {
                    State = _state,
                    Text = text,
                    ToolTip = text,
                    CanResume = false,
                    CanSnooze = CanSkipNow,
                    CanStartBreak = false,
                    IsIdle = false
                };
            }

            case ScheduleState.Snoozed:
            {
                var text = "已暂停 " + FormatDuration(_snooze.RemainingSeconds);
                return new SchedulerStatus
                {
                    State = _state,
                    Text = text,
                    ToolTip = text,
                    CanResume = true,
                    CanSnooze = true,
                    CanStartBreak = true,
                    IsIdle = false
                };
            }

            case ScheduleState.AutoPaused:
            {
                var text = string.IsNullOrEmpty(_autoPauseReason)
                    ? "自动暂停中"
                    : "自动暂停：" + _autoPauseReason;
                return new SchedulerStatus
                {
                    State = _state,
                    Text = text,
                    ToolTip = text,
                    CanResume = true,
                    CanSnooze = true,
                    CanStartBreak = true,
                    IsIdle = false,
                    AutoPauseReason = _autoPauseReason
                };
            }

            case ScheduleState.Working:
            default:
            {
                bool idle = _idleSeconds >= IdleThreshold;
                string text;
                if (idle)
                {
                    text = "空闲中（计时暂停）";
                }
                else
                {
                    var next = NextBreakRemainingSeconds();
                    text = next.HasValue
                        ? "距下次休息 " + FormatDuration(next.Value)
                        : "休息提醒已关闭";
                }

                return new SchedulerStatus
                {
                    State = _state,
                    Text = text,
                    ToolTip = text,
                    CanResume = false,
                    CanSnooze = true,
                    CanStartBreak = true,
                    IsIdle = idle
                };
            }
        }
    }

    public static string FormatDuration(double seconds)
    {
        int total = (int)Math.Ceiling(Math.Max(0, seconds));
        if (total >= 3600)
        {
            return $"{total / 3600}:{total % 3600 / 60:00}:{total % 60:00}";
        }

        return $"{total / 60}:{total % 60:00}";
    }

    private double IdleThreshold => Math.Max(5, _settings.IdleThresholdSeconds);

    private void TickWorking(double deltaSeconds)
    {
        _idleSeconds = _idleDetector.GetIdleSeconds();

        // 自动暂停检测优先于计时累计（若用户正在看视频/开会，本轮不计时）。
        if (AnyAutoPauseEnabled())
        {
            var result = _autoPauseDetector.Evaluate(_settings.AutoPause);

            if (_autoPauseOverridden)
            {
                if (!result.AnyActive)
                {
                    _autoPauseOverridden = false;
                }
            }
            else if (result.ShouldPause)
            {
                EnterAutoPaused(result.Reason);
                return;
            }
        }
        else
        {
            _autoPauseOverridden = false;
        }

        if (_idleSeconds >= IdleThreshold)
        {
            // 用户不在电脑前：不计入工作时长。
            return;
        }

        if (_settings.ShortBreak.Enabled)
        {
            _shortElapsed += deltaSeconds;
        }

        if (_settings.LongBreak.Enabled)
        {
            _longElapsed += deltaSeconds;
        }

        _stats.AddActiveSeconds(deltaSeconds);
        MaybeTriggerBreak();
    }

    private void TickPreBreak(double deltaSeconds)
    {
        _preBreakRemaining -= deltaSeconds;
        if (_preBreakRemaining <= 0)
        {
            var kind = _pendingKind;
            _pendingKind = BreakKind.None;
            PreBreakEnded?.Invoke(this, EventArgs.Empty);
            BeginBreak(kind);
            return;
        }

        PreBreakTick?.Invoke(this, (int)Math.Ceiling(_preBreakRemaining));
    }

    private void TickBreak(double deltaSeconds)
    {
        _breakRemaining -= deltaSeconds;
        _breakElapsed += deltaSeconds;

        if (_breakRemaining <= 0)
        {
            EndBreak(BreakEndReason.Completed);
            return;
        }

        RaiseBreakTick();
    }

    private void TickSnoozed()
    {
        if (_snooze.IsActive)
        {
            return;
        }

        _snooze.End();

        bool startBreakAfter = _snoozeStartBreakAfter;
        BreakKind pendingKind = _snoozePendingKind;
        bool cameFromBreak = _snoozeCameFromBreak;
        ResetSnoozeFlags();

        if (cameFromBreak)
        {
            _shortElapsed = 0;
            _longElapsed = 0;
        }

        SetState(ScheduleState.Working);
        SnoozeEnded?.Invoke(this, EventArgs.Empty);

        if (startBreakAfter && pendingKind != BreakKind.None && !IsBreakDisabled(pendingKind))
        {
            BeginBreak(pendingKind);
        }
    }

    private void TickAutoPaused()
    {
        var result = _autoPauseDetector.Evaluate(_settings.AutoPause);
        if (result.ShouldPause)
        {
            _autoPauseReason = result.Reason;
            return;
        }

        _autoPauseReason = "";
        _autoPauseOverridden = false;
        SetState(ScheduleState.Working);
        AutoPauseChanged?.Invoke(this, new AutoPauseChangedEventArgs { Active = false, Reason = "" });
    }

    private void MaybeTriggerBreak()
    {
        bool shortDue = _settings.ShortBreak.Enabled &&
                        _shortElapsed >= _settings.ShortBreak.IntervalSeconds;
        bool longDue = _settings.LongBreak.Enabled &&
                       _longElapsed >= _settings.LongBreak.IntervalSeconds;

        if (!shortDue && !longDue)
        {
            return;
        }

        BreakKind kind;
        if (longDue)
        {
            kind = BreakKind.Long;
        }
        else if (shortDue)
        {
            // 长休息即将在 5 秒内到期时优先长休息，避免两个遮罩几乎同时出现。
            bool longAlmostDue = _settings.LongBreak.Enabled &&
                                 _longElapsed >= _settings.LongBreak.IntervalSeconds -
                                 Math.Min(LongBreakPriorityWindowSeconds, _settings.LongBreak.IntervalSeconds);
            kind = longAlmostDue ? BreakKind.Long : BreakKind.Short;
        }
        else
        {
            return;
        }

        // 长休息优先时，到期或将在 5 秒内到期的短休息被本次长休息吸收并重置，
        // 不计为跳过。双向对称，避免长休息结束后短休息立即再次弹出。
        if (kind == BreakKind.Long && _settings.ShortBreak.Enabled)
        {
            bool shortAlmostDue = _shortElapsed >=
                _settings.ShortBreak.IntervalSeconds -
                Math.Min(LongBreakPriorityWindowSeconds, _settings.ShortBreak.IntervalSeconds);

            if (shortDue || shortAlmostDue)
            {
                _shortElapsed = 0;
            }
        }

        _stats.IncrementExpected();

        if (_settings.PreBreakNoticeSeconds > 0)
        {
            _pendingKind = kind;
            _preBreakRemaining = _settings.PreBreakNoticeSeconds;
            SetState(ScheduleState.PreBreak);
            PreBreakStarted?.Invoke(this, new PreBreakStartedEventArgs
            {
                Kind = kind,
                RemainingSeconds = _settings.PreBreakNoticeSeconds
            });
        }
        else
        {
            BeginBreak(kind);
        }
    }

    private void BeginBreak(BreakKind kind, bool force = false)
    {
        if (kind == BreakKind.None || (!force && IsBreakDisabled(kind)))
        {
            SetState(ScheduleState.Working);
            return;
        }

        _pendingKind = BreakKind.None;
        _preBreakRemaining = 0;
        _breakKind = kind;
        _breakElapsed = 0;
        _breakDuration = Math.Max(1,
            kind == BreakKind.Long
                ? _settings.LongBreak.DurationSeconds
                : _settings.ShortBreak.DurationSeconds);
        _breakRemaining = _breakDuration;
        _breakSkipPolicy = _settings.SkipPolicy;
        _breakSkipDelaySeconds = Math.Max(0, _settings.SkipDelaySeconds);

        ResetTimer(kind);

        SetState(ScheduleState.Break);

        BreakStarted?.Invoke(this, new BreakStartedEventArgs
        {
            Kind = kind,
            ReminderMode = _settings.ReminderMode,
            DurationSeconds = _breakDuration,
            Message = ResolveBreakMessage(kind),
            ImagePath = _settings.Overlay.CustomImagePath,
            FillMode = _settings.Overlay.FillMode,
            ImageOpacity = _settings.Overlay.Opacity,
            SkipPolicy = _breakSkipPolicy,
            SkipDelaySeconds = _breakSkipDelaySeconds
        });

        RaiseBreakTick();
        _soundService.Play(_settings.SoundEnabled, _settings.SoundFile);
    }

    private void EndBreak(BreakEndReason reason)
    {
        if (_breakKind == BreakKind.None)
        {
            return;
        }

        var kind = _breakKind;
        _breakKind = BreakKind.None;
        _breakRemaining = 0;
        _breakElapsed = 0;

        if (reason == BreakEndReason.Completed)
        {
            _stats.IncrementCompleted();
        }
        else if (reason == BreakEndReason.Skipped)
        {
            _stats.IncrementSkipped();
        }

        if (reason != BreakEndReason.Snoozed)
        {
            SetState(ScheduleState.Working);
        }

        BreakEnded?.Invoke(this, new BreakEndedEventArgs { Kind = kind, Reason = reason });
    }

    private void CancelPreBreak()
    {
        if (_state != ScheduleState.PreBreak)
        {
            return;
        }

        _pendingKind = BreakKind.None;
        _preBreakRemaining = 0;
        SetState(ScheduleState.Working);
        PreBreakEnded?.Invoke(this, EventArgs.Empty);
    }

    private void EnterAutoPaused(string reason)
    {
        _autoPauseReason = reason;
        SetState(ScheduleState.AutoPaused);
        AutoPauseChanged?.Invoke(this, new AutoPauseChangedEventArgs { Active = true, Reason = reason });
    }

    private void RaiseBreakTick()
    {
        int delayRemaining = Math.Max(0, _breakSkipDelaySeconds - (int)Math.Floor(_breakElapsed));
        BreakTick?.Invoke(this, new BreakTickEventArgs
        {
            Kind = _breakKind,
            RemainingSeconds = (int)Math.Ceiling(Math.Max(0, _breakRemaining)),
            ElapsedSeconds = (int)Math.Floor(_breakElapsed),
            DurationSeconds = _breakDuration,
            CanSkip = CanSkipNow,
            SkipDelayRemainingSeconds = CanSkipNow ? 0 : delayRemaining,
            SkipPolicy = _breakSkipPolicy
        });
    }

    private void ResetTimer(BreakKind kind)
    {
        if (kind == BreakKind.Short)
        {
            _shortElapsed = 0;
        }
        else if (kind == BreakKind.Long)
        {
            _longElapsed = 0;
        }
    }

    private bool IsBreakDisabled(BreakKind kind) => kind switch
    {
        BreakKind.Short => !_settings.ShortBreak.Enabled,
        BreakKind.Long => !_settings.LongBreak.Enabled,
        _ => true
    };

    private bool AnyAutoPauseEnabled() =>
        _settings.AutoPause.Fullscreen || _settings.AutoPause.Meeting || _settings.AutoPause.Video;

    private double? NextBreakRemainingSeconds()
    {
        double? next = null;

        if (_settings.ShortBreak.Enabled)
        {
            next = Math.Max(0, _settings.ShortBreak.IntervalSeconds - _shortElapsed);
        }

        if (_settings.LongBreak.Enabled)
        {
            var longRemaining = Math.Max(0, _settings.LongBreak.IntervalSeconds - _longElapsed);
            next = next.HasValue ? Math.Min(next.Value, longRemaining) : longRemaining;
        }

        return next;
    }

    private string ResolveBreakMessage(BreakKind kind)
    {
        var custom = _settings.CustomBreakMessage?.Trim();
        if (!string.IsNullOrEmpty(custom))
        {
            return custom;
        }

        return kind == BreakKind.Long
            ? "站起来走动一下，拉伸肩颈和腰背"
            : "休息一下，看向远处 20 秒";
    }

    private void SetState(ScheduleState newState)
    {
        if (_state == newState)
        {
            return;
        }

        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    private void ResetSnoozeFlags()
    {
        _snoozeStartBreakAfter = false;
        _snoozeCameFromBreak = false;
        _snoozePendingKind = BreakKind.None;
    }
}
