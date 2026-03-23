using Core.Contracts;

namespace IAW.Agents.Memory;

public interface ICodeMemory : IMemoryAgent
{
    static string IAgent.AgentDisplayName => "Code Memory";
    static string IAgent.AgentDescription => "Stores and retrieves code structure, dependency relationships, and implementation details via vector search.";
    static string[] IAgent.AgentCapabilities => ["memory", "code", "search", "recall", "vector", "embedding"];
    static string IAgent.AgentInstructions =>
        "You are Code Memory, the IAW team's record of code structure, dependencies, and implementation details. " +
        "Track code organization, dependency relationships, and key implementation decisions.";
}
