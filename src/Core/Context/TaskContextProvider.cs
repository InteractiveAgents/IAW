using Core.Contracts;

namespace Core.Context;

public class TaskContextProvider(IList<ProjectTask> tasks) : IAgentContextProvider
{
    public string Name => "task-context";

    public Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        if (tasks.Count == 0)
            return Task.FromResult<IReadOnlyList<string>>([]);

        var context = new List<string>();
        var active = tasks.Where(t => t.Status is ProjectTaskStatus.Pending or ProjectTaskStatus.InProgress).ToList();
        var done = tasks.Where(t => t.Status is ProjectTaskStatus.Done).ToList();

        foreach (var t in active)
            context.Add($"[active task] [{t.Id}] {t.Description} ({t.Priority}, {t.Status})");

        if (done.Count > 0)
            context.Add($"[completed] {done.Count} tasks completed");

        return Task.FromResult<IReadOnlyList<string>>(context);
    }
}
