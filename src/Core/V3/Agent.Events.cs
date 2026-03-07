using System.Diagnostics;
using System.Text.RegularExpressions;
using Core.V3.Messages;
using Core.V3.Observability;
using Orleans.Streams;

namespace Core.V3;

public abstract partial class Agent
{
    public virtual Task HandleEventAsync(AgentEvent agentEvent, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentEvent>>(eventLog.ToList());

    protected async Task PublishAsync(string eventName, Dictionary<string, object>? payload = null, CancellationToken ct = default)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.publish");
        activity?.SetTag("event.name", eventName);

        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        var agentEvent = new AgentEvent(
            eventName, this.GetPrimaryKeyString(), correlationId,
            DateTimeOffset.UtcNow, payload ?? []);

        eventLog.Add(agentEvent);
        await WriteStateAsync(ct);

        var streamId = StreamId.Create("agents", eventName);
        var stream = StreamProvider.GetStream<AgentEvent>(streamId);
        await stream.OnNextAsync(agentEvent);

        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", eventName } });
    }

    protected async Task PublishTypedAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : IEvent
    {
        var streamName = EventTypeToStreamName(typeof(TEvent));
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.publish_typed");
        activity?.SetTag("event.name", streamName);
        activity?.SetTag("event.type", typeof(TEvent).Name);

        var agentEvent = new AgentEvent(
            streamName, evt.SourceAgentId, evt.CorrelationId,
            evt.Timestamp, new Dictionary<string, object> { ["typed_payload"] = evt });

        eventLog.Add(agentEvent);
        await WriteStateAsync(ct);

        var streamId = StreamId.Create("agents", streamName);
        var stream = StreamProvider.GetStream<AgentEvent>(streamId);
        await stream.OnNextAsync(agentEvent);

        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", streamName } });
    }

    public static string EventTypeToStreamName(Type eventType)
    {
        var name = eventType.Name;
        if (name.EndsWith("Event")) name = name[..^5];
        else if (name.EndsWith("Command")) name = name[..^7];
        else if (name.EndsWith("Notification")) name = name[..^12];
        return Regex.Replace(name, "(?<!^)([A-Z])", ".$1").ToLowerInvariant();
    }
}
