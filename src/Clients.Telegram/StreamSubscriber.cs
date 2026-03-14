using System.Collections.Concurrent;
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

            var dashboardStream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create("agents", "dashboard.changed"));
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
                StreamId.Create("agents", "wizard.started"));
            await wizardStream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    var wizardId = evt.Payload.GetValueOrDefault("wizardId")?.ToString() ?? "";
                    var prompt = evt.Payload.GetValueOrDefault("prompt")?.ToString() ?? "";
                    var optionsPayload = evt.Payload.GetValueOrDefault("options") switch
                    {
                        string[] arr => arr,
                        IEnumerable<string> seq => seq.ToArray(),
                        _ => Array.Empty<string>()
                    };
                    var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString() ?? "";
                    await botService.SendWizardStepAsync(wizardId, prompt, optionsPayload, projectSlug, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send wizard step to Telegram");
                }
            });

            logger.LogInformation("Subscribed to agent notification, approval, dashboard, and wizard streams");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to agent streams");
        }

        // Keep alive
        await Task.Delay(Timeout.Infinite, ct);
    }

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
}
