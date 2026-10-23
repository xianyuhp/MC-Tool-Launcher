using System.IO;
using System.Windows.Media;

namespace MCTL.Core;

/// <summary>播放启动器目录下 Music 文件夹中的音频，支持 mp3 / wav / wma / m4a。</summary>
public class MusicService
{
    public static MusicService Instance { get; } = new();

    private static readonly string[] Extensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };
    private readonly MediaPlayer _player = new();
    private readonly List<string> _playlist = new();
    private int _index = -1;
    private bool _paused;

    public event Action TrackChanged;
    public event Action StateChanged;
    public event Action PlaylistChanged;

    public string CurrentTrack => _index >= 0 && _index < _playlist.Count ? Path.GetFileNameWithoutExtension(_playlist[_index]) : "未在播放";
    public bool IsPlaying => _player.Source != null && !_paused;
    public IReadOnlyList<string> Playlist => _playlist;

    private MusicService()
    {
        _player.MediaEnded += (_, _) => Next();
        _player.MediaFailed += (_, e) =>
        {
            Logger.Warn("音乐播放失败：" + e.ErrorException?.Message);
            _paused = false;
            _player.Stop();
            StateChanged?.Invoke();
        };
    }

    public void Reload()
    {
        _playlist.Clear();
        var folder = Settings.Current.EffectiveMusicFolder;
        try
        {
            if (Directory.Exists(folder))
                _playlist.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Warn("扫描音乐文件夹失败：" + ex.Message);
        }

        Logger.Info($"音乐库：{_playlist.Count} 首（{folder}）");
        PlaylistChanged?.Invoke();
        if (_playlist.Count == 0) Stop();
        else if (_index >= _playlist.Count) _index = 0;
    }

    public void PlayPause()
    {
        if (_playlist.Count == 0) Reload();
        if (_playlist.Count == 0) return;

        if (_player.Source == null)
        {
            PlayIndex(0);
            return;
        }
        if (_paused)
        {
            _player.Play();
            _paused = false;
        }
        else
        {
            _player.Pause();
            _paused = true;
        }
        StateChanged?.Invoke();
    }

    public void PlayIndex(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        _index = index;
        _paused = false;
        try
        {
            _player.Open(new Uri(_playlist[index]));
            _player.Volume = Settings.Current.MusicVolume / 100.0;
            _player.Play();
            Logger.Info("正在播放：" + _playlist[index]);
        }
        catch (Exception ex)
        {
            Logger.Warn("打开音频失败：" + ex.Message);
        }
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void Next()
    {
        if (_playlist.Count == 0) return;
        PlayIndex((_index + 1) % _playlist.Count);
    }

    public void Previous()
    {
        if (_playlist.Count == 0) return;
        PlayIndex((_index - 1 + _playlist.Count) % _playlist.Count);
    }

    public void Stop()
    {
        _player.Stop();
        _player.Close();
        _paused = false;
        _index = -1;
        TrackChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void ApplyVolume() => _player.Volume = Settings.Current.MusicVolume / 100.0;
}
