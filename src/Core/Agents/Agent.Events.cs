using Core;
using Core.Contracts;
using Core.Messages;
using Core.Observability;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IAW.Core;

public abstract partial class Agent
{
    public Task<List<AgentEvent>> GetEventLog(CancellationToken ct = default)
        => Task.FromResult(durableState.EventLog.ToList());

    protected async Task PublishAsync(string eventName, Dictionary<string, string>? payload = null, CancellationToken ct = default)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.publish");
        activity?.SetTag("event.name", eventName);
        activity?.SetTag("gen_ai.agent.id", this.GetPrimaryKeyString());
        activity?.SetTag("gen_ai.agent.name", DisplayName);

        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        var threadScope = ExtractThreadScope(this.GetPrimaryKeyString());

        payload ??= [];
        payload["utc_timestamp"] = DateTimeOffset.UtcNow.ToString("o");
        if (threadScope is not null)
            payload["thread_id"] = threadScope;

        var agentEvent = new AgentEvent(
            eventName, this.GetPrimaryKeyString(), correlationId,
            DateTimeOffset.UtcNow, payload);

        durableState.EventLog.Add(agentEvent);
        await WriteStateAsync(ct);

        // publish to thread-scoped stream when agent belongs to a thread
        if (threadScope is not null)
        {
            var scopedStreamId = StreamId.Create(IAWConstants.StreamProvider, $"thread/{threadScope}/{eventName}");
            var scopedStream = StreamProvider.GetStream<AgentEvent>(scopedStreamId);
            await scopedStream.OnNextAsync(agentEvent);
        }
        else
        {
            // standalone agents (direct MCP calls) still use global stream
            var streamId = StreamId.Create(IAWConstants.StreamProvider, eventName);
            var stream = StreamProvider.GetStream<AgentEvent>(streamId);
            await stream.OnNextAsync(agentEvent);
        }

        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", eventName }, { "agent.type", GetType().Name } });
    }

    static string? ExtractThreadScope(string grainId)
    {
        var slashIndex = grainId.IndexOf('/');
        return slashIndex > 0 ? grainId[..slashIndex] : null;
    }

    protected async Task PublishToStream<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : IEvent
    {
        var streamName = EventTypeToStreamName(typeof(TEvent));
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.publish_typed");
        activity?.SetTag("event.name", streamName);
        activity?.SetTag("event.type", typeof(TEvent).Name);
        activity?.SetTag("gen_ai.agent.id", this.GetPrimaryKeyString());
        activity?.SetTag("gen_ai.agent.name", DisplayName);

        var threadScope = ExtractThreadScope(this.GetPrimaryKeyString());

        var agentEvent = new AgentEvent(
            streamName, evt.SourceAgentId, evt.CorrelationId,
            evt.Timestamp, new Dictionary<string, string>
            {
                ["typed_payload"] = typeof(TEvent).Name,
                ["thread_id"] = threadScope ?? ""
            });

        durableState.EventLog.Add(agentEvent);
        await WriteStateAsync(ct);

        if (threadScope is not null)
        {
            var scopedStreamId = StreamId.Create(IAWConstants.StreamProvider, $"thread/{threadScope}/{streamName}");
            var scopedStream = StreamProvider.GetStream<TEvent>(scopedStreamId);
            await scopedStream.OnNextAsync(evt);
        }
        else
        {
            var streamId = StreamId.Create(IAWConstants.StreamProvider, streamName);
            var stream = StreamProvider.GetStream<TEvent>(streamId);
            await stream.OnNextAsync(evt);
        }

        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", streamName }, { "agent.type", GetType().Name } });
    }

    // back-compat alias so existing callers of PublishTypedAsync keep compiling
    protected Task PublishTypedAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : IEvent
        => PublishToStream(evt, ct);

    protected async Task PublishToTaskStream<TEvent>(string taskId, TEvent evt, CancellationToken ct = default)
        where TEvent : ITaskStreamEvent
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.publish_task_stream");
        activity?.SetTag("event.type", typeof(TEvent).Name);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("gen_ai.agent.id", this.GetPrimaryKeyString());
        activity?.SetTag("gen_ai.agent.name", DisplayName);

        var streamId = StreamId.Create(IAWConstants.StreamProvider, $"task/{taskId}");
        var stream = StreamProvider.GetStream<TEvent>(streamId);
        await stream.OnNextAsync(evt);

        durableState.EventLog.Add(new AgentEvent(
            typeof(TEvent).Name, evt.SourceAgentId, evt.CorrelationId,
            evt.Timestamp, new Dictionary<string, string> { ["taskId"] = taskId }));
        await WriteStateAsync(ct);

        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", typeof(TEvent).Name }, { "agent.type", GetType().Name } });
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
