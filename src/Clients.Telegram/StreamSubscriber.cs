using Core.Contracts;
using Orleans.Streams;

namespace TelegramClient;

public sealed class StreamSubscriber(
    IClusterClient clusterClient,
    TelegramBotService botService,
    ILogger<StreamSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait for Orleans client to connect
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        try
        {
            var streamProvider = clusterClient.GetStreamProvider("agents");

            var notificationStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create("agents", "notification.sent"));
            await notificationStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    await botService.SendNotificationAsync(evt, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send notification to Telegram");
                }
            });

            var approvalStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create("agents", "approval.requested"));
            await approvalStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var approvalId = evt.Payload.GetValueOrDefault("approvalId")?.ToString() ?? "";
                    var question = evt.Payload.GetValueOrDefault("question")?.ToString() ?? "";
                    var approvalOptions = evt.Payload.GetValueOrDefault("options") as string[] ?? [];
                    var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString() ?? "";
                    await botService.SendApprovalAsync(approvalId, question, approvalOptions, projectSlug, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send approval request to Telegram");
                }
            });

            logger.LogInformation("Subscribed to agent notification and approval streams");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to agent streams");
        }

        // Keep alive
        await Task.Delay(Timeout.Infinite, ct);
    }
}
