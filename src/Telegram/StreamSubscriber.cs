using Core;
using Core.Contracts;
using Orleans.Streams;
using TelegramClient.Services;

namespace TelegramClient;

static class TelegramEvents
{
    public const string NotificationSent = "notification.sent";
    public const string WizardStarted = "wizard.started";
}

public sealed class StreamSubscriber(
    IClusterClient clusterClient,
    NotificationService notificationService,
    ILogger<StreamSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            var streamProvider = clusterClient.GetStreamProvider(IAWConstants.StreamProvider);

            var notificationStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, TelegramEvents.NotificationSent));
            await notificationStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    await notificationService.SendNotificationAsync(evt, ct);
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
                    await notificationService.SendJobResultAsync(projectKey, jobName, result, ct);
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
                    await notificationService.SendProgressAsync(projectKey, taskId, phase, message, ct);
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

        await Task.Delay(Timeout.Infinite, ct);
    }
}
