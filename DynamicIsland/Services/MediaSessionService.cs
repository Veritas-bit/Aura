using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

public sealed record MediaSource(string Id, string Name, bool IsPlaying);

public sealed record MediaSessionInfo(
    string TrackId,
    string Title,
    string Artist,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset CapturedAt,
    byte[]? Artwork,
    string? Error)
{
    public static MediaSessionInfo Empty { get; } =
        new("", "", "", false, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.UtcNow, null, null);

    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);
    public double PlaybackRate { get; init; } = 1;
    public bool CanSeek { get; init; }
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public string AlbumTitle { get; init; } = "";
}

public sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timelineTimer;
    private readonly DispatcherTimer _sourceTimer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _readingTimeline;
    private bool _readingMedia;
    private bool _mediaRefreshPending;
    private TimeSpan _previousTrackPosition;
    private TimeSpan _previousTrackDuration;
    private DateTimeOffset _previousTimelineUpdatedAt;
    private DateTimeOffset _lastTimelineUpdatedAt;
    private DateTimeOffset _trackChangedAt;
    private TimeSpan _timelineStartTime;
    private TimeSpan? _pendingSeekPosition;
    private DateTimeOffset _seekRequestedAt;
    private string? _pinnedSourceId;
    private TimeSpan _timelineMinSeekTime;
    private TimeSpan _timelineMaxSeekTime;
    private bool _disposed;

    public MediaSessionInfo Current { get; private set; } = MediaSessionInfo.Empty;
    public IReadOnlyList<MediaSource> Sources { get; private set; } = Array.Empty<MediaSource>();
    public event Action<MediaSessionInfo>? Changed;
    public event Action<IReadOnlyList<MediaSource>>? SourcesChanged;

    public MediaSessionService()
    {
        _timelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timelineTimer.Tick += async (_, _) => await RefreshTimelineAsync();
        _sourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sourceTimer.Tick += (_, _) =>
        {
            try { RefreshSources(); }
            catch (Exception ex)
            {
                Publish(Current with { Error = $"Не удалось обновить список плееров: {ex.Message}" });
            }
        };
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;
            _timelineTimer.Start();
            _sourceTimer.Start();
            await RefreshCurrentSessionAsync();
        }
        catch (Exception ex)
        {
            Publish(MediaSessionInfo.Empty with
            {
                Error = $"Не удалось подключиться к медиасессиям Windows: {ex.Message}"
            });
        }
    }

    public async Task TogglePlaybackAsync() =>
        await RunCommandAsync(session => session.TryTogglePlayPauseAsync().AsTask());

    public async Task SkipPreviousAsync() =>
        await RunCommandAsync(session => session.TrySkipPreviousAsync().AsTask());

    public async Task SkipNextAsync() =>
        await RunCommandAsync(session => session.TrySkipNextAsync().AsTask());

    public async Task SeekAsync(TimeSpan position)
    {
        var session = _session;
        if (session is null)
        {
            Publish(Current with { Error = "Нет активного медиаплеера" });
            return;
        }
        bool canSeek;
        try
        {
            canSeek = session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled;
        }
        catch (Exception ex)
        {
            Publish(Current with { Error = $"Не удалось проверить поддержку перемотки: {ex.Message}" });
            return;
        }
        if (!canSeek)
        {
            Publish(Current with
            {
                CanSeek = false,
                Error = "Выбранный плеер не поддерживает перемотку через Windows Media Session"
            });
            return;
        }

        var previous = Current;
        position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        if (Current.Duration > TimeSpan.Zero && position > Current.Duration)
            position = Current.Duration;
        var target = _timelineStartTime + position;
        if (_timelineMaxSeekTime > _timelineMinSeekTime)
            target = TimeSpan.FromTicks(Math.Clamp(
                target.Ticks, _timelineMinSeekTime.Ticks, _timelineMaxSeekTime.Ticks));
        var actualPosition = target - _timelineStartTime;
        _pendingSeekPosition = actualPosition;
        _seekRequestedAt = DateTimeOffset.UtcNow;
        Publish(Current with { Position = actualPosition, CapturedAt = _seekRequestedAt, Error = null });
        try
        {
            bool changed = await session.TryChangePlaybackPositionAsync(target.Ticks);
            if (!changed)
            {
                _pendingSeekPosition = null;
                Publish(previous with { Error = "Активный плеер не поддерживает перемотку" });
                return;
            }
            await RefreshTimelineAsync(session);
        }
        catch (Exception ex)
        {
            _pendingSeekPosition = null;
            Publish(previous with { Error = $"Не удалось перемотать трек: {ex.Message}" });
        }
    }

    public async Task SelectSourceAsync(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        _pinnedSourceId = sourceId;
        await RefreshCurrentSessionAsync();
    }

    private void Manager_CurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) =>
        Post(RefreshCurrentSessionAsync);

    private async Task RefreshCurrentSessionAsync()
    {
        if (_disposed || _manager is null) return;
        try
        {
            RefreshSources();
            var next = GetPreferredSession();
            if (!ReferenceEquals(next, _session))
            {
                Unsubscribe(_session);
                Publish(MediaSessionInfo.Empty);
                _session = next;
                _lastTimelineUpdatedAt = DateTimeOffset.MinValue;
                _timelineStartTime = TimeSpan.Zero;
                _timelineMinSeekTime = TimeSpan.Zero;
                _timelineMaxSeekTime = TimeSpan.Zero;
                _pendingSeekPosition = null;
                _trackChangedAt = DateTimeOffset.MinValue;
                Subscribe(_session);
                if (_session is null)
                    return;
                await RefreshMediaPropertiesAsync();
            }

            await RefreshPlaybackAsync();
            await RefreshTimelineAsync();
        }
        catch (Exception ex)
        {
            Publish(Current with { Error = $"Не удалось подключиться к активному плееру: {ex.Message}" });
        }
    }

    private GlobalSystemMediaTransportControlsSession? GetPreferredSession()
    {
        if (_manager is null) return null;
        var sessions = _manager.GetSessions();
        if (!string.IsNullOrWhiteSpace(_pinnedSourceId))
        {
            var pinnedSession = sessions.FirstOrDefault(session =>
                string.Equals(session.SourceAppUserModelId, _pinnedSourceId, StringComparison.Ordinal));
            if (pinnedSession is not null) return pinnedSession;
            _pinnedSourceId = null;
        }

        foreach (var session in sessions)
        {
            if (session.GetPlaybackInfo().PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return session;
        }

        return _manager.GetCurrentSession();
    }

    private void RefreshSources()
    {
        if (_manager is null) return;
        var sources = _manager.GetSessions()
            .Select(session => new MediaSource(
                session.SourceAppUserModelId,
                GetSourceName(session.SourceAppUserModelId),
                session.GetPlaybackInfo().PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing))
            .Where(source => !string.IsNullOrWhiteSpace(source.Id))
            .GroupBy(source => source.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(source => source.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (Sources.SequenceEqual(sources)) return;
        Sources = sources;
        SourcesChanged?.Invoke(Sources);
    }

    private static string GetSourceName(string sourceId)
    {
        var appId = sourceId.Split('!').Last();
        if (sourceId.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (sourceId.Contains("apple", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        if (sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            sourceId.Contains("edge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (sourceId.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (sourceId.Contains("yandex", StringComparison.OrdinalIgnoreCase)) return "Яндекс Музыка";
        return System.IO.Path.GetFileNameWithoutExtension(appId.Replace('_', '.'))
            .Replace('.', ' ')
            .Trim();
    }

    private void Subscribe(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null) return;
        session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
        session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
        session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;
    }

    private void Unsubscribe(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null) return;
        session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
        session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
        session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged;
    }

    private void Session_MediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) =>
        Post(() => RefreshMediaPropertiesAsync(sender));

    private void Session_PlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) =>
        Post(() => RefreshPlaybackAsync(sender));

    private void Session_TimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) =>
        Post(() => RefreshTimelineAsync(sender));

    private void Post(Func<Task> action)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess())
            _ = action();
        else
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () => await action()));
    }

    private async Task RefreshMediaPropertiesAsync(
        GlobalSystemMediaTransportControlsSession? expectedSession = null)
    {
        var session = _session;
        if (_disposed || session is null ||
            (expectedSession is not null && !ReferenceEquals(expectedSession, session)))
            return;
        if (_readingMedia)
        {
            _mediaRefreshPending = true;
            return;
        }

        _readingMedia = true;
        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (_disposed || !ReferenceEquals(session, _session)) return;
            if (_mediaRefreshPending) return;

            var artwork = await ReadArtworkAsync(media.Thumbnail);
            if (_disposed || !ReferenceEquals(session, _session)) return;
            if (_mediaRefreshPending) return;

            var id = $"{session.SourceAppUserModelId}\n{media.Title}\n{media.Artist}";
            var trackChanged = !string.Equals(id, Current.TrackId, StringComparison.Ordinal);
            if (trackChanged)
            {
                _previousTrackPosition = Current.Position;
                _previousTrackDuration = Current.Duration;
                _previousTimelineUpdatedAt = _lastTimelineUpdatedAt;
                _trackChangedAt = DateTimeOffset.UtcNow;
                _lastTimelineUpdatedAt = DateTimeOffset.MinValue;
                _timelineStartTime = TimeSpan.Zero;
                _pendingSeekPosition = null;
            }
            Publish(Current with
            {
                TrackId = id,
                SourceId = session.SourceAppUserModelId,
                SourceName = GetSourceName(session.SourceAppUserModelId),
                Title = media.Title ?? "",
                Artist = media.Artist ?? "",
                AlbumTitle = media.AlbumTitle ?? "",
                Position = trackChanged ? TimeSpan.Zero : Current.Position,
                Duration = trackChanged ? TimeSpan.Zero : Current.Duration,
                CapturedAt = trackChanged ? DateTimeOffset.UtcNow : Current.CapturedAt,
                Artwork = artwork,
                Error = trackChanged ? null : Current.Error
            });
        }
        catch (Exception ex)
        {
            Publish(Current with { Error = $"Не удалось получить данные трека: {ex.Message}" });
        }
        finally
        {
            _readingMedia = false;
            if (_mediaRefreshPending && !_disposed)
            {
                _mediaRefreshPending = false;
                Post(() => RefreshMediaPropertiesAsync());
            }
        }
    }

    private static async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        using var stream = await thumbnail.OpenReadAsync();
        if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024) return null;

        uint size = checked((uint)stream.Size);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        var bytes = new byte[checked((int)size)];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private async Task RefreshPlaybackAsync(
        GlobalSystemMediaTransportControlsSession? expectedSession = null)
    {
        var session = _session;
        if (_disposed || session is null ||
            (expectedSession is not null && !ReferenceEquals(expectedSession, session)))
            return;
        try
        {
            var playback = session.GetPlaybackInfo();
            Publish(Current with
            {
                IsPlaying = playback.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                PlaybackRate = playback.PlaybackRate is double rate && rate > 0 ? rate : 1,
                CanSeek = playback.Controls.IsPlaybackPositionEnabled
            });
            await RefreshTimelineAsync(session);
        }
        catch (Exception ex)
        {
            Publish(Current with { Error = $"Не удалось обновить состояние воспроизведения: {ex.Message}" });
        }
    }

    private async Task RefreshTimelineAsync(
        GlobalSystemMediaTransportControlsSession? expectedSession = null)
    {
        var session = _session;
        if (_disposed || session is null || _readingTimeline ||
            (expectedSession is not null && !ReferenceEquals(expectedSession, session)))
            return;

        _readingTimeline = true;
        try
        {
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var startTime = timeline.StartTime;
            var duration = timeline.EndTime - startTime;
            var position = timeline.Position - startTime;
            var minSeekTime = timeline.MinSeekTime;
            var maxSeekTime = timeline.MaxSeekTime;
            var rawUpdatedAt = timeline.LastUpdatedTime;
            var updatedAt = rawUpdatedAt;
            var now = DateTimeOffset.UtcNow;
            bool waitingForSeek = false;
            bool seekConfirmed = false;
            string? timelineError = null;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration) position = duration;
            if (updatedAt == DateTimeOffset.MinValue || updatedAt > now)
                updatedAt = now;

            if (_pendingSeekPosition is { } seekTarget)
            {
                seekConfirmed = Math.Abs((position - seekTarget).TotalSeconds) <= 0.75;
                bool seekTimedOut = now - _seekRequestedAt > TimeSpan.FromSeconds(2);
                if (seekConfirmed)
                    _pendingSeekPosition = null;
                else if (seekTimedOut)
                {
                    _pendingSeekPosition = null;
                    timelineError = "Плеер не подтвердил перемотку";
                }
                else
                {
                    position = seekTarget;
                    waitingForSeek = true;
                }
            }

            if (_trackChangedAt != DateTimeOffset.MinValue)
            {
                bool timelineReset = position <= TimeSpan.FromSeconds(3) ||
                                     position < _previousTrackPosition - TimeSpan.FromSeconds(1);
                bool durationChanged = duration > TimeSpan.Zero &&
                                       _previousTrackDuration > TimeSpan.Zero &&
                                       Math.Abs((duration - _previousTrackDuration).TotalSeconds) > 1;
                bool freshTimeline = rawUpdatedAt > _previousTimelineUpdatedAt;
                bool transitionResolved = freshTimeline || timelineReset || durationChanged ||
                                          now - _trackChangedAt >= TimeSpan.FromSeconds(2);
                if (!transitionResolved) return;
                _trackChangedAt = DateTimeOffset.MinValue;
            }

            if (rawUpdatedAt != DateTimeOffset.MinValue &&
                _lastTimelineUpdatedAt != DateTimeOffset.MinValue &&
                rawUpdatedAt < _lastTimelineUpdatedAt)
                return;
            if (playback.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                now > updatedAt && !waitingForSeek)
            {
                var currentPlaybackRate = playback.PlaybackRate is double currentRate && currentRate > 0
                    ? currentRate
                    : 1;
                position += TimeSpan.FromTicks((long)((now - updatedAt).Ticks * currentPlaybackRate));
            }
            if (position > duration && duration > TimeSpan.Zero) position = duration;

            if (rawUpdatedAt != DateTimeOffset.MinValue)
                _lastTimelineUpdatedAt = rawUpdatedAt;
            _timelineStartTime = startTime;
            _timelineMinSeekTime = minSeekTime;
            _timelineMaxSeekTime = maxSeekTime;
            Publish(Current with
            {
                IsPlaying = playback.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                PlaybackRate = playback.PlaybackRate is double playbackRateValue && playbackRateValue > 0
                    ? playbackRateValue
                    : 1,
                CanSeek = playback.Controls.IsPlaybackPositionEnabled,
                Position = position,
                Duration = duration,
                CapturedAt = now,
                Error = timelineError ?? (seekConfirmed ? null : Current.Error)
            });
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Publish(Current with { Error = $"Не удалось обновить позицию трека: {ex.Message}" });
        }
        finally
        {
            _readingTimeline = false;
        }
    }

    private async Task RunCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        var session = _session;
        if (session is null)
        {
            Publish(Current with { Error = "Нет активного медиаплеера" });
            return;
        }
        try
        {
            bool succeeded = await command(session);
            if (!succeeded)
            {
                Publish(Current with { Error = "Активный плеер отклонил команду" });
                return;
            }
            Publish(Current with { Error = null });
            await RefreshPlaybackAsync(session);
            await RefreshTimelineAsync(session);
        }
        catch (Exception ex)
        {
            Publish(Current with { Error = $"Не удалось управлять воспроизведением: {ex.Message}" });
        }
    }

    private void Publish(MediaSessionInfo info)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => Publish(info)));
            return;
        }
        Current = info;
        Changed?.Invoke(info);
    }

    public void Dispose()
    {
        _disposed = true;
        _timelineTimer.Stop();
        _sourceTimer.Stop();
        Unsubscribe(_session);
        if (_manager is not null)
            _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;
    }
}
