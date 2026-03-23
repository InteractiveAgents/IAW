using Core.Contracts;

namespace IAW.Agents.Memory;

public interface IProjectMemory : IMemoryAgent
{
    static string IAgent.AgentDisplayName => "Project Memory";
    static string IAgent.AgentDescription => "Tracks project conventions, architecture decisions, and agreements, surfacing relevant context via vector search.";
    static string[] IAgent.AgentCapabilities => ["memory", "project", "architecture", "decisions", "search", "recall"];
    static string IAgent.AgentInstructions =>
        "You are Project Memory, the IAW team's record of conventions, architecture decisions, and agreements. " +
        "Track how the project evolves and surface relevant decisions when queried.";
}