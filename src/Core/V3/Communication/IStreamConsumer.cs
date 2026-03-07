using Core.V3.Messages;
using Orleans.Streams;

namespace Core.V3.Communication;

public interface IStreamConsumer<TEvent> where TEvent : IEvent
{
    Task OnStreamEventAsync(TEvent evt, StreamSequenceToken? token);
}
