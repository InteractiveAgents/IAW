using Core.Contracts;

namespace IAW.Agents.Orchestration;

public interface INotificationAgent : IAgent
{
    Task SendNotification(NotificationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationRecord>> GetRecentNotifications(int count = 10, CancellationToken ct = default);
}

[GenerateSerializer]
public record NotificationRequest(
    [property: Id(0)] string Title,
    [property: Id(1)] string Message,
    [property: Id(2)] NotificationChannel Channel,
    [property: Id(3)] NotificationSeverity Severity,
    [property: Id(4)] string? TargetAgentId);

[GenerateSerializer]
public record NotificationRecord(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] string Message,
    [property: Id(3)] NotificationChannel Channel,
    [property: Id(4)] NotificationSeverity Severity,
    [property: Id(5)] DateTimeOffset SentAt,
    [property: Id(6)] bool Delivered);

public enum NotificationChannel { Dashboard, Telegram, Email, Log }

public enum NotificationSeverity { Info, Warning, Error, Critical }
