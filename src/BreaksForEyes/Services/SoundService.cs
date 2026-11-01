using System.IO;
using System.Media;
using System.Windows.Media;

namespace BreaksForEyes.Services;

/// <summary>
/// 提示音（FR-6）。默认播放系统提示音；用户指定文件时用 WPF MediaPlayer 播放
/// （支持 wav/mp3/wma 等系统解码格式），失败时回退系统提示音。
/// </summary>
public sealed class SoundService : IDisposable
{
    private MediaPlayer? _player;

    public void Play(bool enabled, string filePath)
    {
        if (!enabled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            try
            {
                _player ??= new MediaPlayer();
                _player.Close();
                _player.Open(new Uri(Path.GetFullPath(filePath)));
                _player.Volume = 1.0;
                _player.Play();
                return;
            }
            catch
            {
                // 回退系统提示音。
            }
        }

        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // 无音频设备时静默失败。
        }
    }

    public void PlayPreview(string filePath)
    {
        Play(enabled: true, filePath);
    }

    public void Dispose()
    {
        try
        {
            _player?.Close();
        }
        catch
        {
            // 忽略。
        }

        _player = null;
    }
}
