using Core.Contracts;
using Core.Messages;

namespace Core.Context;

public class TaskStreamContextProvider(IGrainFactory grainFactory) : IAgentContextProvider
{
    public string Name => "TaskStream";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        var agent = grainFactory.GetGrain<IAgent>(agentId);
        var events = await agent.GetEventLog(ct);

        var taskEvents = events
            .Where(e => e.Payload.ContainsKey("taskId"))
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .Select(e =>
            {
                var taskId = e.Payload["taskId"]?.ToString() ?? "unknown";
                return $"[{e.EventName}] task={taskId} at {e.Timestamp:HH:mm:ss}";
            })
            .ToList();

        return taskEvents;
    }
}
