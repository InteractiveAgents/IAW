using Core.Contracts;

namespace IAW.Agents.Memory;

public interface IPatternMemory : IMemoryAgent
{
    static string IAgent.AgentDisplayName => "Pattern Memory";
    static string IAgent.AgentDescription => "Stores proven code and design patterns, recommending them for similar problems via vector search.";
    static string[] IAgent.AgentCapabilities => ["memory", "patterns", "design", "search", "recall", "vector"];
    static string IAgent.AgentInstructions =>
        "You are Pattern Memory, the IAW team's catalog of proven code and design patterns. " +
        "Store patterns that work well and recommend them for similar problems when queried.";
}