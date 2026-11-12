using NAudio.CoreAudioApi;

namespace DynamicIsland.Services;

/// <summary>
/// Event-driven Windows volume monitor. Observes master-volume or mute changes
/// even if AURA does not have keyboard focus, and rebinds on output-device change.
/// Unlike cached COM polling, the endpoint callback reports external volume keys.
/// </summary>
public sealed class AudioVolumeMonitorService : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private bool _disposed;

    /// <summary>Volume in percent (0..100), and mute state.</summary>
    public event Action<double, bool>? VolumeChanged;
    public event Action? OutputDeviceChanged;
    public double CurrentVolumePercent { get; private set; }
    public bool IsMuted { get; private set; }
    public bool HeadphonesConnected { get; private set; }

    private static bool IsHeadphoneEndpoint(string name)
    {
        // Windows endpoint friendly names vary by driver and locale. Avoid claiming
        // that ordinary speakers are headphones when the output type is uncertain.
        string[] keywords =
        {
            "headphone", "headset", "earphone", "earbud", "airpods",
            "наушник", "гарнитур", "наушники", "buds"
        };
        return keywords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public AudioVolumeMonitorService()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            RefreshDefaultDevice();
        }
        catch
        {
            // No sound endpoint (or audio subsystem unavailable): UI keeps working.
        }
    }

    /// <summary>
    /// Called at a low frequency to follow headset/speaker changes;
    /// no popup is shown merely because the default device changed.
    /// </summary>
    public void RefreshDefaultDevice()
    {
        if (_disposed || _enumerator is null) return;

        MMDevice? next = null;
        try
        {
            next = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_device != null && string.Equals(next.ID, _device.ID,
                    StringComparison.Ordinal))
            {
                next.Dispose();
                return;
            }

            DetachDevice();
            _device = next;
            CurrentVolumePercent = Math.Clamp(Math.Round(
                next.AudioEndpointVolume.MasterVolumeLevelScalar * 100), 0, 100);
            IsMuted = next.AudioEndpointVolume.Mute;
            HeadphonesConnected = IsHeadphoneEndpoint(next.FriendlyName ?? "");
            next.AudioEndpointVolume.OnVolumeNotification += OnEndpointVolumeChanged;
            OutputDeviceChanged?.Invoke();
        }
        catch
        {
            if (!ReferenceEquals(next, _device))
                next?.Dispose();
            DetachDevice();
        }
    }

    private void OnEndpointVolumeChanged(AudioVolumeNotificationData args)
    {
        if (_disposed) return;

        // This callback can run on a Core Audio worker thread:
        // never interact with COM or WPF from here.
        double percent = Math.Clamp(Math.Round(args.MasterVolume * 100), 0, 100);
        CurrentVolumePercent = percent;
        IsMuted = args.Muted;
        VolumeChanged?.Invoke(percent, args.Muted);
    }

    private void DetachDevice()
    {
        if (_device is null) return;

        try { _device.AudioEndpointVolume.OnVolumeNotification -= OnEndpointVolumeChanged; }
        catch { /* device can disappear while being unplugged */ }
        try { _device.Dispose(); }
        catch { }
        _device = null;
        HeadphonesConnected = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachDevice();
        _enumerator?.Dispose();
        _enumerator = null;
    }
}
