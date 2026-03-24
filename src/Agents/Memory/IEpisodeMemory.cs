using Core.Contracts;

namespace IAW.Agents.Memory;

public interface IEpisodeMemory : IMemoryAgent
{
    static string IAgent.AgentDisplayName => "Episode Memory";
    static string IAgent.AgentDescription => "Records task workflows and outcomes, enabling retrieval of past episode context via vector search.";
    static string[] IAgent.AgentCapabilities => ["memory", "episode", "workflow", "search", "recall", "vector"];
    static string IAgent.AgentInstructions =>
        "You are Episode Memory, the IAW team's record of task workflows and outcomes. " +
        "Store what steps were taken, their results, and how tasks completed. Surface relevant episodes when queried.";
}