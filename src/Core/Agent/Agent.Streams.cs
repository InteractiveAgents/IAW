using System.Diagnostics;
using Core.Communication;
using Core.Contracts;
using Core.Observability;
using Orleans.Streams;

namespace IAW.Core;

public abstract partial class Agent
{
    public async Task PublishToStream(AgentEvent evt, CancellationToken ct = default)
    {
        eventLog.Add(evt);
        await WriteStateAsync(ct);
        var streamId = StreamId.Create("agents", evt.EventName);
        var stream = StreamProvider.GetStream<AgentEvent>(streamId);
        await stream.OnNextAsync(evt);
        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", evt.EventName } });
    }

    public Task<IReadOnlyList<string>> GetActiveSubscriptions(CancellationToken ct = default)
    {
        var subs = GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>))
            .Select(i => EventTypeToStreamName(i.GetGenericArguments()[0]))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(subs);
    }

    private async Task SubscribeToStreamConsumerInterfaces()
    {
        var consumerInterfaces = GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>))
            .ToList();

        if (consumerInterfaces.Count == 0)
            return;

        foreach (var iface in consumerInterfaces)
        {
            var eventType = iface.GetGenericArguments()[0];
            var streamName = EventTypeToStreamName(eventType);
            var streamId = StreamId.Create("agents", streamName);
            var stream = StreamProvider.GetStream<AgentEvent>(streamId);

            await stream.SubscribeAsync((Func<AgentEvent, StreamSequenceToken, Task>)(async (evt, _) =>
            {
                using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.handle_stream_event");
                activity?.SetTag("event.name", evt.EventName);
                activity?.SetTag("agent.type", GetType().Name);

                var sw = Stopwatch.StartNew();
                await this.HandleEvent(evt, AgentCancellation);
                sw.Stop();

                AgentTelemetry.EventsHandled.Add(1, new TagList { { "event.name", evt.EventName } });
                AgentTelemetry.EventHandleDuration.Record(sw.Elapsed.TotalSeconds, new TagList { { "event.name", evt.EventName } });
            }));
        }
    }
}
