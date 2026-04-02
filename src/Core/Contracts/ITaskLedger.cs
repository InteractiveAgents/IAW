namespace Core.Contracts;

public interface ITaskLedger : IGrainWithStringKey
{
    Task AppendAsync(TaskEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<TaskEvent>> GetEventsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TaskEvent>> GetEventsSinceAsync(DateTimeOffset since, CancellationToken ct = default);
    Task<string> GetContextBlockAsync(int maxEvents = 15, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
