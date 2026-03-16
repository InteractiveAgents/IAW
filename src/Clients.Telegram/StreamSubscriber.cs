using System.Collections.Concurrent;
using Core;
using Core.Contracts;
using Orleans.Streams;

namespace TelegramClient;

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
                StreamId.Create(IAWConstants.StreamProvider, "notification.sent"));
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
                StreamId.Create(IAWConstants.StreamProvider, "approval.requested"));
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
                StreamId.Create(IAWConstants.StreamProvider, "dashboard.changed"));
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
                StreamId.Create(IAWConstants.StreamProvider, "wizard.started"));
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

            var progressStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, "orchestration.progress"));
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

            var completedStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create(IAWConstants.StreamProvider, "orchestration.completed"));
            await completedStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var taskId = evt.Payload.GetValueOrDefault("TaskId")?.ToString() ?? "";
                    var summary = evt.Payload.GetValueOrDefault("Summary")?.ToString() ?? "";
                    logger.LogInformation("Orchestration completed [{TaskId}]: {Summary}", taskId, summary);
                    await botService.SendNotificationAsync(evt, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send orchestration completed to Telegram");
                }
            });

            logger.LogInformation("Subscribed to agent notification, approval, dashboard, wizard, and orchestration streams");
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
                        DateTimeOffset.UtcNow, new Dictionary<string, object>
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
