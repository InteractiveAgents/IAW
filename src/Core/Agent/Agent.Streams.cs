using System.Diagnostics;
using System.Reflection;
using Core.Communication;
using Core.Contracts;
using Core.Messages;
using Core.Observability;
using Orleans.Streams;

namespace IAW.Core;

public abstract partial class Agent
{
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

            // subscribe to typed Stream<TEvent> and dispatch to OnStreamEventAsync
            var subscribeMethod = typeof(Agent)
                .GetMethod(nameof(SubscribeTyped), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(eventType);

            await (Task)subscribeMethod.Invoke(this, [streamId, streamName])!;
        }
    }

    private async Task SubscribeTyped<TEvent>(StreamId streamId, string streamName) where TEvent : class, IEvent
    {
        var stream = StreamProvider.GetStream<TEvent>(streamId);
        var consumer = (IStreamConsumer<TEvent>)this;

        await stream.SubscribeAsync(async (evt, token) =>
        {
            using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.handle_stream_event");
            activity?.SetTag("event.name", streamName);
            activity?.SetTag("event.type", typeof(TEvent).Name);
            activity?.SetTag("agent.type", GetType().Name);

            var sw = Stopwatch.StartNew();
            await consumer.OnStreamEventAsync(evt, token);
            sw.Stop();

            AgentTelemetry.EventsHandled.Add(1, new TagList { { "event.name", streamName } });
            AgentTelemetry.EventHandleDuration.Record(sw.Elapsed.TotalSeconds, new TagList { { "event.name", streamName } });
        });
    }
}
