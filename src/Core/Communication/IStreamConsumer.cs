using Core.Messages;
using Orleans.Streams;

namespace Core.Communication;

// implementing this auto-subscribes the agent to the typed event stream
// events arrive directly via OnStreamEventAsync(TEvent, token)
public interface IStreamConsumer<TEvent> where TEvent : IEvent
{
    Task OnStreamEventAsync(TEvent evt, StreamSequenceToken? token);
}
