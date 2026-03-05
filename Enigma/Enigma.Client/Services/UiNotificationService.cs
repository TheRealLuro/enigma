using System.Collections.Concurrent;

namespace Enigma.Client.Services;

public enum UiNotificationLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record UiNotificationMessage(
    string Id,
    string Message,
    UiNotificationLevel Level,
    DateTimeOffset CreatedAt,
    TimeSpan Duration
);

public sealed class UiNotificationService
{
    private readonly ConcurrentDictionary<string, UiNotificationMessage> _active = new();

    public event Action<UiNotificationMessage>? NotificationPublished;

    public void Info(string message, int seconds = 4) => Publish(message, UiNotificationLevel.Info, seconds);
    public void Success(string message, int seconds = 4) => Publish(message, UiNotificationLevel.Success, seconds);
    public void Warning(string message, int seconds = 5) => Publish(message, UiNotificationLevel.Warning, seconds);
    public void Error(string message, int seconds = 6) => Publish(message, UiNotificationLevel.Error, seconds);

    public void Publish(string message, UiNotificationLevel level = UiNotificationLevel.Info, int seconds = 4)
    {
        var normalizedMessage = (message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return;
        }

        var durationSeconds = Math.Max(2, Math.Min(20, seconds));
        var notification = new UiNotificationMessage(
            Id: Guid.NewGuid().ToString("N"),
            Message: normalizedMessage,
            Level: level,
            CreatedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromSeconds(durationSeconds));
        _active[notification.Id] = notification;
        NotificationPublished?.Invoke(notification);
    }

    public IReadOnlyList<UiNotificationMessage> GetActive()
    {
        return _active.Values
            .OrderByDescending(entry => entry.CreatedAt)
            .ToList();
    }

    public void Remove(string id)
    {
        var normalizedId = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        _active.TryRemove(normalizedId, out _);
    }
}
