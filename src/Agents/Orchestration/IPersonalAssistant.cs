using Core.Contracts;

namespace IAW.Agents.Orchestration;

public interface IPersonalAssistant : IAgent
{
    Task<string> GetTeamStatusAsync(CancellationToken ct = default);
    Task<string[]> GetActiveTasksAsync(CancellationToken ct = default);
}
