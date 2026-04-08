using Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public class TaskLedgerContextProvider(IGrainFactory grainFactory, string taskId, ILogger<TaskLedgerContextProvider>? logger = null) : IAgentContextProvider
{
    public string Name => "task-ledger";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        try
        {
            var ledger = grainFactory.GetGrain<ITaskLedger>(taskId);
            var block = await ledger.GetContextBlockAsync(maxEvents: 15, ct);

            if (string.IsNullOrEmpty(block))
                return [];

            return [block];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Task ledger context provider failed for task {TaskId}", taskId);
            return [];
        }
    }
}
