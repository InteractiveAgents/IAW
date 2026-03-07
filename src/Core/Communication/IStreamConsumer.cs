using IAW.Core.Messages;
using Orleans.Streams;

namespace IAW.Core.Communication;

public interface IStreamConsumer<TEvent> where TEvent : IEvent
{
    Task OnStreamEventAsync(TEvent evt, StreamSequenceToken? token);
}
