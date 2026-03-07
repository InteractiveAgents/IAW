using Core.V3.Messages;

namespace Core.V3.Communication;

public interface INotifier<TNotification> where TNotification : INotification
{
    Task NotifyAsync(TNotification notification, CancellationToken ct = default);
    Task SubscribeObserverAsync(IAgentObserver<TNotification> observer);
    Task UnsubscribeObserverAsync(IAgentObserver<TNotification> observer);
}
