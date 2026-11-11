using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;
using Microsoft.Win32;

namespace DynamicIsland.Views;

/// <summary>Многофункциональный плеер (Windows Media Foundation).</summary>
public partial class PlayerPanel : UserControl
{
    private readonly MediaPlayer _player = new();
    private readonly DispatcherTimer _posTimer;
    private readonly List<string> _files = new();
    private readonly Random _rnd = new();
    private int _index = -1;
    private bool _playing;
    private bool _shuffle;
    private bool _repeat;
    private bool _seeking;
    private bool _opened;

    public PlayerPanel()
    {
        InitializeComponent();

        // обложка в цвет компаньона
        void ApplyEyeColor()
        {
            try
            {
                CoverEyes.SetLineTheme(ThemeService.IsLight);
                CoverEyes.SetEyeColor((Color)ColorConverter.ConvertFromString(SettingsService.Current.EyeColor));
            }
            catch { }
        }
        ApplyEyeColor();
        SettingsService.Changed += ApplyEyeColor;

        foreach (var f in SettingsService.Current.Playlist)
            if (File.Exists(f)) AddToList(f);

        VolumeSlider.Value = SettingsService.Current.PlayerVolume * 100;
        _player.Volume = SettingsService.Current.PlayerVolume;
        _player.MediaEnded += (_, _) => Next(auto: true);
        _player.MediaOpened += (_, _) =>
        {
            _opened = true;
            if (_player.NaturalDuration.HasTimeSpan)
            {
                SeekSlider.Maximum = _player.NaturalDuration.TimeSpan.TotalSeconds;
                TimeTotal.Text = FormatTime(_player.NaturalDuration.TimeSpan);
            }
        };

        _posTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _posTimer.Tick += (_, _) =>
        {
            if (_playing && !_seeking && _opened)
            {
                SeekSlider.Value = _player.Position.TotalSeconds;
                TimeCurrent.Text = FormatTime(_player.Position);
            }
        };
        _posTimer.Start();

        SeekSlider.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
        SeekSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            _seeking = false;
            if (_opened) _player.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        };
    }

    // ---------- Плейлист ----------

    private void Add_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Аудио и видео|*.mp3;*.wav;*.m4a;*.wma;*.flac;*.ogg;*.mp4;*.m4v|Все файлы|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames) AddToList(f);
        Persist();
        if (_index < 0 && _files.Count > 0) PlayAt(0);
    }

    private void Remove_Click(object s, RoutedEventArgs e)
    {
        int i = PlaylistBox.SelectedIndex;
        if (i < 0) return;
        _files.RemoveAt(i);
        PlaylistBox.Items.RemoveAt(i);
        Persist();
        if (i == _index) Stop();
        else if (i < _index) _index--;
    }

    private void Playlist_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (PlaylistBox.SelectedIndex >= 0) PlayAt(PlaylistBox.SelectedIndex);
    }

    private void AddToList(string file)
    {
        _files.Add(file);
        PlaylistBox.Items.Add(new TextBlock
        {
            Text = Path.GetFileNameWithoutExtension(file),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
    }

    private void Persist()
    {
        SettingsService.Current.Playlist = new List<string>(_files);
        SettingsService.Save();
    }

    // ---------- Воспроизведение ----------

    private void SetPlayIcon(bool playing)
    {
        if (Application.Current.FindResource(playing ? "ic_pause" : "ic_play") is Geometry g)
            PlayIcon.Data = g;
    }

    private void PlayAt(int index)
    {
        if (index < 0 || index >= _files.Count) return;
        _index = index;
        PlaylistBox.SelectedIndex = index;
        try
        {
            _player.Open(new Uri(_files[index]));
            _player.Play();
            _playing = true;
            _opened = false;
            SetPlayIcon(true);
            TrackTitle.Text = Path.GetFileNameWithoutExtension(_files[index]);
            TrackPath.Text = _files[index];
        }
        catch (Exception ex)
        {
            TrackTitle.Text = "Не удалось открыть файл";
            TrackPath.Text = ex.Message;
        }
    }

    private void Stop()
    {
        _player.Stop();
        _playing = false;
        SetPlayIcon(false);
        TrackTitle.Text = "Ничего не играет";
        TrackPath.Text = "";
        TimeCurrent.Text = "0:00";
    }

    private void PlayPause_Click(object s, RoutedEventArgs e)
    {
        if (_playing) { _player.Pause(); _playing = false; SetPlayIcon(false); }
        else
        {
            if (_opened) { _player.Play(); _playing = true; SetPlayIcon(true); }
            else if (_files.Count > 0) PlayAt(Math.Max(0, _index));
        }
    }

    private void Prev_Click(object s, RoutedEventArgs e)
    {
        if (_files.Count == 0) return;
        PlayAt((_index - 1 + _files.Count) % _files.Count);
    }

    private void Next_Click(object s, RoutedEventArgs e) => Next(auto: false);

    private void Next(bool auto)
    {
        if (_files.Count == 0) return;
        if (auto && _repeat) { PlayAt(_index); return; }
        int next = _shuffle ? _rnd.Next(_files.Count) : (_index + 1) % _files.Count;
        PlayAt(next);
    }

    private void Shuffle_Click(object s, RoutedEventArgs e) => _shuffle = !_shuffle;
    private void Repeat_Click(object s, RoutedEventArgs e) => _repeat = !_repeat;

    private void Seek_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking) TimeCurrent.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
    }

    private void Volume_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        _player.Volume = VolumeSlider.Value / 100.0;
        SettingsService.Current.PlayerVolume = _player.Volume;
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
