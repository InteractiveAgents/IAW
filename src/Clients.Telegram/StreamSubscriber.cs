using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _dashboardDebounce = new();

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

            var approvalStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, IAWConstants.Events.ApprovalRequested));
            await approvalStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var approvalId = evt.Payload.GetValueOrDefault("approvalId")?.ToString() ?? "";
                    var question = evt.Payload.GetValueOrDefault("question")?.ToString() ?? "";
                    var approvalOptions = ResolveStringArray(evt.Payload.GetValueOrDefault("options"));
                    var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString() ?? "";
                    await botService.SendApprovalAsync(approvalId, question, approvalOptions, projectSlug, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send approval request to Telegram");
                }
            });

            var dashboardStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, IAWConstants.Events.DashboardChanged));
            await dashboardStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var projectKey = evt.Payload.GetValueOrDefault("projectKey")?.ToString() ?? "";
                    var renderedMarkdown = evt.Payload.GetValueOrDefault("renderedMarkdown")?.ToString() ?? "";
                    ScheduleDebouncedDashboardUpdate(projectKey, renderedMarkdown, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to handle dashboard.changed event");
                }
            });

            var wizardStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, TelegramEvents.WizardStarted));
            await wizardStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var wizardId = evt.Payload.GetValueOrDefault("wizardId")?.ToString() ?? "";
                    var prompt = evt.Payload.GetValueOrDefault("prompt")?.ToString() ?? "";
                    var optionsPayload = ResolveStringArray(evt.Payload.GetValueOrDefault("options"));
                    var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString() ?? "";
                    await botService.SendWizardStepAsync(wizardId, prompt, optionsPayload, projectSlug, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send wizard step to Telegram");
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
                    var taskId = evt.Payload.GetValueOrDefault("TaskId")?.ToString() ?? "";
                    var message = evt.Payload.GetValueOrDefault("Message")?.ToString() ?? "";
                    logger.LogInformation("Orchestration progress [{TaskId}]: {Message}", taskId, message);
                    await botService.SendNotificationAsync(evt, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send orchestration progress to Telegram");
                }
            });

            logger.LogInformation("Subscribed to agent notification, approval, dashboard, wizard, job completed, and orchestration streams");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to agent streams");
        }

        // Keep alive
        await Task.Delay(Timeout.Infinite, ct);
    }

    private static string[] ResolveStringArray(object? value) => value switch
    {
        string[] arr => arr,
        object[] objs => [.. objs.Select(o => o?.ToString() ?? "")],
        IEnumerable<string> seq => [.. seq],
        IEnumerable<object> seq => [.. seq.Select(o => o?.ToString() ?? "")],
        _ => []
    };

    private void ScheduleDebouncedDashboardUpdate(string projectKey, string renderedMarkdown, CancellationToken ct)
    {
        // Cancel any pending update for this project
        if (_dashboardDebounce.TryRemove(projectKey, out var previousCts))
            previousCts.Cancel();

        var debounceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _dashboardDebounce[projectKey] = debounceCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), debounceCts.Token);
                _dashboardDebounce.TryRemove(projectKey, out _);

                logger.LogInformation("Publishing dashboard update for project {ProjectKey}", projectKey);
                await botService.SendNotificationAsync(
                    new AgentEvent("dashboard.updated", projectKey, Guid.NewGuid().ToString(),
                        DateTimeOffset.UtcNow, new Dictionary<string, string>
                        {
                            ["renderedMarkdown"] = renderedMarkdown
                        }), ct);
            }
            catch (OperationCanceledException)
            {
                // debounced — a newer update superseded this one
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send dashboard update for project {ProjectKey}", projectKey);
            }
        }, ct);
    }

    private async Task<int?> ResolveTopicIdAsync(string projectSlug, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectSlug)) return null;
        var parts = projectSlug.Split('/');
        if (parts.Length < 2) return null;
        var userId = parts[0];
        var slug = parts[1];
        var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
        return await userProfile.GetTopicId(slug, ct);
    }
}
