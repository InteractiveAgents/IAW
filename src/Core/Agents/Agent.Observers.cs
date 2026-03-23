namespace IAW.Core;

// Phase 2: typed observer dispatch
public abstract partial class Agent
{
    private readonly HashSet<IGrainObserver> _observers = [];

    public Task SubscribeObserverAsync(IGrainObserver observer, CancellationToken ct = default)
    {
        _observers.Add(observer);
        return Task.CompletedTask;
    }

    public Task UnsubscribeObserverAsync(IGrainObserver observer, CancellationToken ct = default)
    {
        _observers.Remove(observer);
        return Task.CompletedTask;
    }
}