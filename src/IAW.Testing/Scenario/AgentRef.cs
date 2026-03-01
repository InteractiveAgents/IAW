using Core;

namespace IAW.Testing.Scenario;

public sealed class AgentRef(Func<string, IAgent> agentFactory, string agentId)
{
    public string AgentId { get; } = agentId;

    public IAgent Resolve() => agentFactory(AgentId);
}
