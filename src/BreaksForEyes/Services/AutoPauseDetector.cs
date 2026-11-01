using BreaksForEyes.Models;

namespace BreaksForEyes.Services;

public readonly record struct AutoPauseResult(
    bool FullscreenActive,
    bool MeetingActive,
    bool VideoActive,
    bool ShouldPause,
    string Reason)
{
    public bool AnyActive => FullscreenActive || MeetingActive || VideoActive;
}

/// <summary>
/// 自动暂停检测（FR-10）。全部为启发式判断，宁可漏判也不误打断：
/// - 全屏应用：前景窗口覆盖所在显示器且无标题栏；
/// - 会议/通话：前景进程命中白名单且窗口标题含会议关键词，或标题命中 Web 会议关键词；
/// - 视频播放：前景进程命中播放器白名单，或全屏 + 默认音频设备有峰值。
/// </summary>
public sealed class AutoPauseDetector
{
    private static readonly HashSet<string> MeetingProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Teams", "ms-teams", "zoom", "Zoom", "webex", "webexmta", "CiscoCollabHost",
        "wemeetapp", "wemeet", "DingTalk", "Feishu", "Lark", "slack", "Discord"
    };

    private static readonly HashSet<string> VideoProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc", "mpv", "PotPlayerMini64", "PotPlayerMini", "mpc-hc64", "mpc-hc",
        "mpc-be64", "mpc-be", "wmplayer", "Video.UI", "bilibili", "哔哩哔哩",
        "iqiyi", "QQLive", "youku", "QQPlayer", "KMPlayer", "Netflix", "Hulu",
        "Disney+", "Prime Video"
    };

    private static readonly string[] MeetingTitleHints =
    {
        "meeting", "call", "huddle", "webinar", "会议", "通话", "共享屏幕"
    };

    private static readonly string[] WebMeetingTitleHints =
    {
        "google meet", "meet.google.com", "zoom meeting", "腾讯会议", "webex meeting",
        "飞书会议", "钉钉会议", "teams meeting"
    };

    private readonly ForegroundMonitor _foregroundMonitor = new();
    private readonly AudioActivityDetector _audioDetector = new();

    private DateTime _lastAudioCheck = DateTime.MinValue;
    private bool _lastAudioActive;

    public AutoPauseResult Evaluate(AutoPauseSettings settings)
    {
        bool anyCategoryEnabled = settings.Fullscreen || settings.Meeting || settings.Video;
        if (!anyCategoryEnabled)
        {
            return default;
        }

        var foreground = _foregroundMonitor.GetForeground();
        if (foreground is null)
        {
            return new AutoPauseResult(false, false, false, false, "");
        }

        var info = foreground.Value;
        bool fullscreen = settings.Fullscreen && info.IsFullscreen;
        bool meeting = settings.Meeting && IsMeeting(info);
        bool video = settings.Video && IsVideo(info);

        var reasons = new List<string>(3);
        if (fullscreen)
        {
            reasons.Add("全屏应用");
        }

        if (meeting)
        {
            reasons.Add("会议/通话");
        }

        if (video)
        {
            reasons.Add("视频播放");
        }

        return new AutoPauseResult(
            fullscreen,
            meeting,
            video,
            fullscreen || meeting || video,
            string.Join("、", reasons));
    }

    private static bool IsMeeting(ForegroundInfo info)
    {
        if (string.IsNullOrEmpty(info.ProcessName))
        {
            return false;
        }

        if (MeetingProcesses.Contains(info.ProcessName) &&
            ContainsAny(info.WindowTitle, MeetingTitleHints))
        {
            return true;
        }

        return ContainsAny(info.WindowTitle, WebMeetingTitleHints);
    }

    private bool IsVideo(ForegroundInfo info)
    {
        if (!string.IsNullOrEmpty(info.ProcessName) && VideoProcesses.Contains(info.ProcessName))
        {
            return true;
        }

        // 浏览器内全屏视频等：全屏 + 默认音频设备回放活跃。
        return info.IsFullscreen && AudioIsActive();
    }

    private bool AudioIsActive()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastAudioCheck).TotalSeconds < 3)
        {
            return _lastAudioActive;
        }

        _lastAudioCheck = now;
        _lastAudioActive = _audioDetector.GetPeakValue() > 0.0005f;
        return _lastAudioActive;
    }

    private static bool ContainsAny(string text, string[] needles)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
