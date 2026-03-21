using Core;
using Core.Contracts;
using Orleans.Streams;

namespace TelegramClient;

static class TelegramEvents
{
    public const string NotificationSent = "notification.sent";
    public const string WizardStarted = "wizard.started";
}

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
            var streamProvider = clusterClient.GetStreamProvider(IAWConstants.StreamProvider);

            var notificationStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, TelegramEvents.NotificationSent));
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

            var jobCompletedStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, IAWConstants.Events.JobCompleted));
            await jobCompletedStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var projectKey = evt.Payload.GetValueOrDefault("projectKey")?.ToString() ?? "";
                    var jobName = evt.Payload.GetValueOrDefault("jobName")?.ToString() ?? "";
                    var result = evt.Payload.GetValueOrDefault("result")?.ToString() ?? "";
                    await botService.SendJobResultAsync(projectKey, jobName, result, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send job result to Telegram");
                }
            });

            var progressStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, IAWConstants.Events.OrchestrationProgress));
            await progressStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var projectKey = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.ProjectKey)?.ToString() ?? "";
                    var taskId = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.TaskId)?.ToString() ?? "";
                    var phase = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.Phase)?.ToString() ?? "";
                    var message = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.Message)?.ToString() ?? "";
                    await botService.SendProgressAsync(projectKey, taskId, phase, message, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send orchestration progress to Telegram");
                }
            });

            logger.LogInformation("Subscribed to notification, job completed, and orchestration progress streams");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to agent streams");
        }

        // Keep alive
        await Task.Delay(Timeout.Infinite, ct);
    }
}
