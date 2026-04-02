namespace Core.Contracts.Notifications;

[GenerateSerializer]
public record UINotification(
    [property: Id(0)] string Type,
    [property: Id(1)] string? TaskId,
    [property: Id(2)] string Summary,
    [property: Id(3)] DateTimeOffset Timestamp,
    [property: Id(4)] string? FilePath = null,
    [property: Id(5)] int? PercentComplete = null,
    [property: Id(6)] string? Severity = null,
    [property: Id(7)] string? ApprovalId = null,
    [property: Id(8)] IReadOnlyList<string>? Options = null)
{
    public static UINotification TaskCompleted(string taskId, string summary, string? filePath = null)
        => new("task.completed", taskId, summary, DateTimeOffset.UtcNow, FilePath: filePath);

    public static UINotification Progress(string taskId, string message, int percentComplete)
        => new("progress", taskId, message, DateTimeOffset.UtcNow, PercentComplete: percentComplete);

    public static UINotification Alert(string severity, string message)
        => new("alert", null, message, DateTimeOffset.UtcNow, Severity: severity);

    public static UINotification ApprovalNeeded(string approvalId, string question, IReadOnlyList<string> options)
        => new("approval", null, question, DateTimeOffset.UtcNow, ApprovalId: approvalId, Options: options);
}
