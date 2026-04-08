using Core.Contracts;
using Orleans.Journaling;

namespace Core.Context;

public class TaskStreamContextProvider(IDurableList<AgentEvent> eventLog) : IAgentContextProvider
{
    public string Name => "TaskStream";

    public Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        var taskEvents = eventLog
            .Where(e => e.Payload.ContainsKey("taskId"))
            .OrderByDescending(e => e.Timestamp)
            .Take(10)
            .Select(e =>
            {
                var taskId = e.Payload["taskId"]?.ToString() ?? "unknown";
                return $"[{e.EventName}] task={taskId} at {e.Timestamp:HH:mm:ss}";
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(taskEvents);
    }
}