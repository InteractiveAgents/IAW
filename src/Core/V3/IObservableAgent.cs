namespace Core.V3;

public interface IObservableAgent : IAgent
{
    Task SubscribeObserverAsync(IGrainObserver observer, CancellationToken ct);
    Task UnsubscribeObserverAsync(IGrainObserver observer, CancellationToken ct);
}
