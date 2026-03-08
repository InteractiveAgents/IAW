using Core.Messages;
using Orleans.Streams;

namespace Core.Communication;

// marker interface: implementing this auto-subscribes the agent to the typed event stream
// actual events arrive via the HandleEvent(AgentEvent, ct) override
public interface IStreamConsumer<TEvent> where TEvent : IEvent
{
    Task OnStreamEventAsync(TEvent evt, StreamSequenceToken? token);
}
