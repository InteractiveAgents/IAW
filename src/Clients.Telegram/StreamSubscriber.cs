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
            var stream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create("agents", "notification.sent"));

            await stream.SubscribeAsync(async (evt, token) =>
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

            logger.LogInformation("Subscribed to agent notification stream");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to notification stream");
        }

        // Keep alive
        await Task.Delay(Timeout.Infinite, ct);
    }
}
