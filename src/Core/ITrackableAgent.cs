namespace IAW.Core;

public interface ITrackableAgent : IAgent
{
    Task StartTrackingAsync(string name, TrackingItem item, TimeSpan interval, CancellationToken ct);
    Task StopTrackingAsync(string name, CancellationToken ct);
}
