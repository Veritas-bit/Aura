using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

public partial class PlayerPanel : UserControl
{
    private readonly MediaSessionService _media;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _volumeTimer;
    private bool _seeking;
    private bool _updatingVolume;
    private bool _updatingSources;
    private string _trackId = "";
    private byte[]? _artworkBytes;
    private bool _isPlaying;
    private double _playbackRate = 1;
    private TimeSpan _positionAnchor;
    private TimeSpan _duration;
    private DateTimeOffset _anchorTime = DateTimeOffset.UtcNow;

    public PlayerPanel(MediaSessionService media)
    {
        InitializeComponent();
        _media = media;
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _positionTimer.Tick += (_, _) => RenderPosition();
        _volumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _volumeTimer.Tick += (_, _) => RefreshVolume();

        Loaded += (_, _) =>
        {
            _media.Changed += OnMediaChanged;
            _media.SourcesChanged += UpdateSources;
            _positionTimer.Start();
            _volumeTimer.Start();
            RefreshVolume();
            UpdateSources(_media.Sources);
            UpdateMedia(_media.Current);
        };
        Unloaded += (_, _) =>
        {
            _media.Changed -= OnMediaChanged;
            _media.SourcesChanged -= UpdateSources;
            _positionTimer.Stop();
            _volumeTimer.Stop();
        };

        SeekSlider.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
        SeekSlider.PreviewMouseLeftButtonUp += (_, _) => CommitSeek();
        SeekSlider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => CommitSeek()));
        SeekSlider.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                _seeking = true;
        };
        SeekSlider.PreviewKeyUp += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                CommitSeek();
        };
    }

    private void OnMediaChanged(MediaSessionInfo info)
    {
        if (Dispatcher.CheckAccess())
            UpdateMedia(info);
        else Dispatcher.Invoke(() => UpdateMedia(info));
    }

    private void UpdateSources(IReadOnlyList<MediaSource> sources)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateSources(sources));
            return;
        }
        if (!IsLoaded) return;

        _updatingSources = true;
        try
        {
            if (SourceSelector.ItemsSource is not IEnumerable<MediaSource> currentSources ||
                !currentSources.SequenceEqual(sources))
                SourceSelector.ItemsSource = sources;
            SourceSelector.SelectedValue = _media.Current.SourceId;
            SourceSelector.Visibility = sources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updatingSources = false; }
    }

    private async void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSources || SourceSelector.SelectedValue is not string sourceId) return;
        await _media.SelectSourceAsync(sourceId);
    }

    private void UpdateMedia(MediaSessionInfo info)
    {
        if (!IsLoaded) return;
        if (!string.Equals(SourceSelector.SelectedValue as string, info.SourceId, StringComparison.Ordinal))
        {
            _updatingSources = true;
            try { SourceSelector.SelectedValue = info.SourceId; }
            finally { _updatingSources = false; }
        }

        bool newTrack = !string.Equals(_trackId, info.TrackId, StringComparison.Ordinal);
        bool artworkChanged = !ReferenceEquals(_artworkBytes, info.Artwork);
        double playbackRate = info.PlaybackRate > 0 ? info.PlaybackRate : 1;
        bool playbackChanged = _isPlaying != info.IsPlaying ||
                               Math.Abs(_playbackRate - playbackRate) > 0.001;
        _trackId = info.TrackId;
        if (newTrack)
            _duration = info.Duration;
        else if (info.Duration > TimeSpan.Zero &&
                 Math.Abs((info.Duration - _duration).TotalSeconds) > 1)
            _duration = info.Duration;

        TrackTitle.Text = info.HasTrack ? info.Title : "Запустите музыку в любом приложении";
        TrackArtist.Text = info.HasTrack
            ? (string.IsNullOrWhiteSpace(info.Artist) ? "Исполнитель не указан" : info.Artist)
            : "AURA покажет текущий трек и синхронизирует управление";
        SessionStatus.Text = info.Error ?? (info.HasTrack
            ? (info.IsPlaying
                ? (info.CanSeek ? "Воспроизведение" : "Воспроизведение · перемотка недоступна")
                : (info.CanSeek ? "На паузе" : "На паузе · перемотка недоступна"))
            : "Нет активной медиасессии");
        SetPlayIcon(info.IsPlaying);

        if (newTrack || artworkChanged)
        {
            _artworkBytes = info.Artwork;
            SetArtwork(info.Artwork);
        }

        var now = DateTimeOffset.UtcNow;
        if (newTrack)
        {
            _positionAnchor = info.Position;
            _anchorTime = info.CapturedAt;
            SeekSlider.Value = 0;
        }
        else if (playbackChanged)
        {
            _positionAnchor = GetCurrentPosition(now);
            _anchorTime = now;
            _playbackRate = playbackRate;
        }
        else if (!info.IsPlaying)
        {
            _positionAnchor = info.Position;
            _anchorTime = now;
        }
        else
        {
            var predicted = GetCurrentPosition(now);
            var reported = info.Position + (info.IsPlaying && now > info.CapturedAt
                ? TimeSpan.FromTicks((long)((now - info.CapturedAt).Ticks * playbackRate))
                : TimeSpan.Zero);
            var correction = reported - predicted;
            if (Math.Abs(correction.TotalSeconds) > 0.75)
            {
                _positionAnchor = reported;
                _anchorTime = now;
            }
            else if (Math.Abs(correction.TotalSeconds) > 0.08)
            {
                var boundedCorrection = TimeSpan.FromMilliseconds(
                    Math.Clamp(correction.TotalMilliseconds * 0.35, -60, 60));
                _positionAnchor = predicted + boundedCorrection;
                _anchorTime = now;
            }
        }
        _isPlaying = info.IsPlaying;
        _playbackRate = playbackRate;
        if (_duration > TimeSpan.Zero)
        {
            SeekSlider.Maximum = _duration.TotalSeconds;
            SeekSlider.IsEnabled = true;
            TimeTotal.Text = FormatTime(_duration);
        }
        else
        {
            SeekSlider.Maximum = 1;
            SeekSlider.IsEnabled = false;
            TimeTotal.Text = "0:00";
        }
        SeekSlider.IsEnabled = _duration > TimeSpan.Zero && info.CanSeek;
        SeekSlider.ToolTip = info.CanSeek
            ? "Перемотка активного медиаплеера"
            : "Выбранный плеер не предоставляет управление перемоткой Windows";

        RenderPosition();
    }

    private void SetArtwork(byte[]? bytes)
    {
        BitmapImage? image = null;
        if (bytes is { Length: > 0 })
        {
            image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
        }

        TrackCover.Source = image;
        ArtworkBackdrop.Source = image;
        TrackCover.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        CoverFallback.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private TimeSpan GetCurrentPosition(DateTimeOffset now)
    {
        var position = _positionAnchor;
        if (_isPlaying)
            position += TimeSpan.FromTicks((long)((now - _anchorTime).Ticks * _playbackRate));
        if (_duration > TimeSpan.Zero && position > _duration) position = _duration;
        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private void RenderPosition()
    {
        if (_seeking) return;
        var position = GetCurrentPosition(DateTimeOffset.UtcNow);
        SeekSlider.Value = Math.Clamp(position.TotalSeconds, SeekSlider.Minimum, SeekSlider.Maximum);
        TimeCurrent.Text = FormatTime(position);
    }

    private void RefreshVolume()
    {
        double volume = SystemControlService.GetVolumeLevel();
        _updatingVolume = true;
        VolumeSlider.IsEnabled = volume >= 0;
        if (volume >= 0)
        {
            VolumeSlider.Value = volume;
            VolumeText.Text = $"{volume:0}%";
        }
        else
        {
            VolumeText.Text = "N/A";
        }

        bool muted = SystemControlService.GetMute();
        if (Application.Current.FindResource(muted ? "ic_volume_x" : "ic_volume_2") is Geometry geometry)
            VolumeIcon.Data = geometry;
        _updatingVolume = false;
    }

    private void SetPlayIcon(bool playing)
    {
        if (Application.Current.FindResource(playing ? "ic_pause" : "ic_play") is Geometry geometry)
            PlayIcon.Data = geometry;
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e) =>
        await _media.TogglePlaybackAsync();

    private async void Prev_Click(object sender, RoutedEventArgs e) =>
        await _media.SkipPreviousAsync();

    private async void Next_Click(object sender, RoutedEventArgs e) =>
        await _media.SkipNextAsync();

    private void Seek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking) TimeCurrent.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
    }

    private async void CommitSeek()
    {
        if (!_seeking) return;
        _seeking = false;
        _positionAnchor = TimeSpan.FromSeconds(SeekSlider.Value);
        _anchorTime = DateTimeOffset.UtcNow;
        await _media.SeekAsync(_positionAnchor);
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingVolume && VolumeSlider.IsEnabled)
        {
            SystemControlService.SetVolumeLevel(VolumeSlider.Value);
            VolumeText.Text = $"{VolumeSlider.Value:0}%";
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        SystemControlService.SetMute(!SystemControlService.GetMute());
        RefreshVolume();
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
