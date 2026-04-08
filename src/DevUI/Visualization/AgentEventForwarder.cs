using Core;
using Core.Contracts;
using Microsoft.AspNetCore.SignalR;
using Orleans.Streams;

namespace DevUI.Visualization;

// subscribes to Orleans agent event streams and forwards to SignalR clients
public class AgentEventForwarder(
    IClusterClient cluster,
    IHubContext<AgentVisualizationHub> hub,
    ILogger<AgentEventForwarder> logger) : BackgroundService
{
    // well-known event names to subscribe to
    static readonly string[] EventNames =
    [
        "file.read", "file.written", "file.created", "file.copied",
        "file.moved", "file.deleted", "file.uploaded",
        "archive.created", "archive.extracted",
        "command.completed", "command.failed",
        "powershell.completed", "powershell.failed",
        "directories.compared",
        IAWConstants.Events.OrchestrationProgress,
        IAWConstants.Events.JobCompleted,
        IAWConstants.Events.ApprovalRequested
    ];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // wait for Orleans client to connect
        await Task.Delay(3000, ct);

        var streamProvider = cluster.GetStreamProvider(IAWConstants.StreamProvider);

        foreach (var eventName in EventNames)
        {
            var streamId = StreamId.Create(IAWConstants.StreamProvider, eventName);
            var stream = streamProvider.GetStream<AgentEvent>(streamId);

            await stream.SubscribeAsync(async (evt, _) =>
            {
                var payload = new
                {
                    eventName = evt.EventName,
                    sourceAgentId = evt.SourceAgentId,
                    correlationId = evt.CorrelationId,
                    timestamp = evt.Timestamp.ToString("o"),
                    threadId = evt.Payload.GetValueOrDefault("thread_id", ""),
                    payload = evt.Payload
                };

                logger.LogDebug("Forwarding event {Event} from {Agent}", evt.EventName, evt.SourceAgentId);
                await hub.Clients.All.SendAsync("AgentEvent", payload, ct);
            });
        }

        logger.LogInformation("AgentEventForwarder subscribed to {Count} global event streams", EventNames.Length);

        // keep alive
        await Task.Delay(Timeout.Infinite, ct);
    }
}

// intercepts all grain-to-grain calls and broadcasts to visualization
public class VisualizationCallFilter(IHubContext<AgentVisualizationHub> hub) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var targetId = context.TargetId.ToString();
        var sourceId = System.Diagnostics.Activity.Current?.GetTagItem("orleans.grain.id")?.ToString();
        var methodName = context.ImplementationMethod?.Name ?? "unknown";

        await context.Invoke();

        if (sourceId is not null && sourceId != targetId)
        {
            var payload = new
            {
                source = sourceId,
                target = targetId,
                method = methodName,
                timestamp = DateTimeOffset.UtcNow.ToString("o")
            };

            try
            {
                await hub.Clients.All.SendAsync("GrainCall", payload);
            }
            catch
            {
                // don't let visualization errors affect grain calls
            }
        }
    }
}
