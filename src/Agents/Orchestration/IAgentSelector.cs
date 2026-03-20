using Core.Contracts;

namespace IAW.Agents.Orchestration;

public interface IAgentSelector : IAgent
{
    Task<SelectionResult> SelectAsync(string userRequest, CancellationToken ct = default);
}
