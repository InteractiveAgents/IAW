using IAW.Core.Messages;

namespace IAW.Core.Communication;

public interface INotifier<TNotification> where TNotification : INotification
{
    Task NotifyAsync(TNotification notification, CancellationToken ct = default);
    Task SubscribeObserverAsync(IAgentObserver<TNotification> observer);
    Task UnsubscribeObserverAsync(IAgentObserver<TNotification> observer);
}
