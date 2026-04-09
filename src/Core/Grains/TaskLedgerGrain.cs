using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Core.Grains;

[GrainType(IAWConstants.GrainTypes.TaskLedger)]
public class TaskLedgerGrain(
    [FromKeyedServices("events")] IDurableList<TaskEvent> events)
    : DurableGrain, ITaskLedger
{
    public async Task AppendAsync(TaskEvent evt, CancellationToken ct = default)
    {
        events.Add(evt);
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TaskEvent>>(events.ToList());

    public Task<IReadOnlyList<TaskEvent>> GetEventsSinceAsync(DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TaskEvent>>(
            events.Where(e => e.Timestamp >= since).ToList());

    public Task<string> GetContextBlockAsync(int maxEvents = 15, CancellationToken ct = default)
    {
        var recent = events.Count > maxEvents
            ? events.Skip(events.Count - maxEvents).ToList()
            : events.ToList();

        if (recent.Count == 0)
            return Task.FromResult(string.Empty);

        var lines = recent.Select(e => e.ToContextLine());
        return Task.FromResult($"[Task activity]\n{string.Join("\n", lines)}");
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        events.Clear();
        await WriteStateAsync(ct);
    }
}
