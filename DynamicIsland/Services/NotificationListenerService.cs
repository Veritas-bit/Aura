using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland.Services;

public sealed record DesktopNotification(
    string AppName, string Title, string Body, DateTimeOffset CreatedAt);

/// <summary>
/// Reads Windows toast notifications only after explicit user consent.
/// This API requires package identity and the userNotificationListener manifest capability.
/// </summary>
public sealed class NotificationListenerService : IDisposable
{
    private UserNotificationListener? _listener;
    private bool _subscribed;
    private bool _disposed;

    public event Action<DesktopNotification>? NotificationReceived;

    public static bool IsPackaged
    {
        get
        {
            try
            {
                _ = Package.Current.Id;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<bool?> StartAsync(bool requestAccess)
    {
        if (_disposed || !IsPackaged) return null;

        try
        {
            _listener ??= UserNotificationListener.Current;
            var status = _listener.GetAccessStatus();
            if (requestAccess && status == UserNotificationListenerAccessStatus.Unspecified)
                status = await _listener.RequestAccessAsync();

            if (status != UserNotificationListenerAccessStatus.Allowed)
                return false;

            if (!_subscribed)
            {
                _listener.NotificationChanged += Listener_NotificationChanged;
                _subscribed = true;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Listener_NotificationChanged(
        UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (_disposed || args.ChangeKind != UserNotificationChangedKind.Added) return;

        try
        {
            var notification = sender.GetNotification(args.UserNotificationId);
            if (notification is null) return;

            string appName = notification.AppInfo?.DisplayInfo?.DisplayName ?? "Уведомление";
            var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) return;

            var textElements = binding.GetTextElements();
            string title = textElements.FirstOrDefault()?.Text ?? appName;
            string body = string.Join(" ", textElements.Skip(1)
                .Select(element => element.Text)
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
                return;

            NotificationReceived?.Invoke(new DesktopNotification(
                appName, title, body, notification.CreationTime));
        }
        catch
        {
            // A malformed notification must not stop the listener for other apps.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_listener is not null && _subscribed)
            _listener.NotificationChanged -= Listener_NotificationChanged;
        _subscribed = false;
    }
}
