using Core.V3.Messages;

namespace Core.V3.Communication;

public interface IStreamProducer<TEvent> where TEvent : IEvent
{
    Task PublishToStreamAsync(TEvent evt, CancellationToken ct = default);
}
