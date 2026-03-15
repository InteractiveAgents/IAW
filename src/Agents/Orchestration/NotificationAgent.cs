using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Orchestration;

public class NotificationAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), INotificationAgent
{
    protected override string DisplayName => "Notification Hub";
    protected override string Instructions => """
        You are the Notification Hub, the IAW team's alert and messaging center. Aggregate events and route notifications to appropriate channels.

        CAPABILITIES:
        - Send notifications with configurable title, message, channel, and severity
        - Route notifications: Dashboard and Log channels deliver immediately; Telegram and Email pending external integration
        - Retrieve recent notifications with filtering
        - Track notification history in state

        CHANNELS & SEVERITY:
        Channels: Dashboard, Log, Telegram, Email
        Severity levels: Critical (route immediately), High, Medium, Low
        Critical alerts bypass normal routing and go to all available channels.

        OUTPUT FORMAT:
        Confirmation: "Notification sent to {channel}: {title}"
        History: list notifications by timestamp, newest first

        RULES:
        - Route critical severity alerts immediately to all connected channels
        - For Dashboard and Log: deliver without external dependencies
        - For Telegram and Email: deliver only when integration is active
        - Keep notification text concise (under 200 characters)
        - Always record notification in state for history and audit
        """;

    public async Task SendNotification(NotificationRequest request, CancellationToken ct = default)
    {
        var record = new NotificationRecord(
            Guid.NewGuid().ToString("N"),
            request.Title,
            request.Message,
            request.Channel,
            request.Severity,
            DateTimeOffset.UtcNow,
            RouteNotification(request));

        State[$"notif-{record.Id}"] = new StateEntry($"notif-{record.Id}", JsonSerializer.Serialize(record));
        await WriteStateAsync(ct);

        await PublishAsync("notification.sent", new Dictionary<string, object>
        {
            ["Channel"] = request.Channel.ToString(),
            ["Severity"] = request.Severity.ToString(),
            ["Title"] = request.Title
        }, ct);
    }

    public Task<IReadOnlyList<NotificationRecord>> GetRecentNotifications(int count = 10, CancellationToken ct = default)
    {
        var notifications = State
            .Where(kvp => kvp.Key.StartsWith("notif-"))
            .Select(kvp => JsonSerializer.Deserialize<NotificationRecord>(kvp.Value.Value.ToString()!))
            .Where(n => n is not null)
            .Cast<NotificationRecord>()
            .OrderByDescending(n => n.SentAt)
            .Take(count)
            .ToList();
        return Task.FromResult<IReadOnlyList<NotificationRecord>>(notifications);
    }

    private static bool RouteNotification(NotificationRequest request)
    {
        // dashboard and log channels are always delivered
        // telegram and email require external integration (not yet connected)
        return request.Channel is NotificationChannel.Dashboard or NotificationChannel.Log;
    }
}
