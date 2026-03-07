using IAW.Core.Messages;

namespace IAW.Core.Communication;

public interface IStreamProducer<TEvent> where TEvent : IEvent
{
    Task PublishToStreamAsync(TEvent evt, CancellationToken ct = default);
}
