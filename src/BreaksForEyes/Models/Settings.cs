namespace BreaksForEyes.Models;

/// <summary>
/// 短休息（20-20-20）配置。对应 DESIGN.md §8.3 的 shortBreak 节点。
/// </summary>
public sealed class ShortBreakSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 20 * 60;
    public int DurationSeconds { get; set; } = 20;
}

/// <summary>
/// 长休息（起身活动）配置。对应 DESIGN.md §8.3 的 longBreak 节点。
/// </summary>
public sealed class LongBreakSettings
{
    public bool Enabled { get; set; } = false;
    public int IntervalSeconds { get; set; } = 60 * 60;
    public int DurationSeconds { get; set; } = 90;
}

/// <summary>遮罩自定义图片配置（FR-15）。</summary>
public sealed class OverlaySettings
{
    public string CustomImagePath { get; set; } = "";
    /// <summary>"perScreen" | "spanStrip"</summary>
    public string FillMode { get; set; } = "perScreen";
    /// <summary>0.0 - 1.0，仅作用于图片层。</summary>
    public double Opacity { get; set; } = 0.9;
}

/// <summary>自动暂停检测开关（FR-10）。</summary>
public sealed class AutoPauseSettings
{
    public bool Fullscreen { get; set; } = true;
    public bool Meeting { get; set; } = true;
    public bool Video { get; set; } = true;
}

/// <summary>
/// 应用配置。字段与 DESIGN.md §8.3 的 settings.json schema 一一对应
/// （属性名经 camelCase 序列化后与设计文档一致）。
/// </summary>
public sealed class Settings
{
    public ShortBreakSettings ShortBreak { get; set; } = new();
    public LongBreakSettings LongBreak { get; set; } = new();
    /// <summary>"overlay" | "toast"</summary>
    public string ReminderMode { get; set; } = "overlay";
    /// <summary>0 | 5 | 20</summary>
    public int PreBreakNoticeSeconds { get; set; } = 20;
    public string CustomBreakMessage { get; set; } = "休息一下，看向远处 20 秒";
    public bool SoundEnabled { get; set; } = true;
    public string SoundFile { get; set; } = "";
    public OverlaySettings Overlay { get; set; } = new();
    public int IdleThresholdSeconds { get; set; } = 60;
    public AutoPauseSettings AutoPause { get; set; } = new();
    /// <summary>"immediate" | "delay" | "none"</summary>
    public string SkipPolicy { get; set; } = "immediate";
    public int SkipDelaySeconds { get; set; } = 5;
    /// <summary>开机自启，默认关闭（opt-in，由用户在设置中主动开启）。</summary>
    public bool LaunchAtLogin { get; set; } = false;
    /// <summary>"zh-CN" | "en-US"（FR-14 为 P2，当前 UI 固定中文，仅保存该设置）。</summary>
    public string Language { get; set; } = "zh-CN";

    public static Settings CreateDefault() => new();
}
