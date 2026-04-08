using Core.Contracts;

namespace IAW.Agents.Memory;

public interface IUserMemory : IMemoryAgent
{
    static string IAgent.AgentDisplayName => "User Memory";
    static string IAgent.AgentDescription => "Stores personal facts, preferences, and corrections about users, enabling personalized long-term recall.";
    static string[] IAgent.AgentCapabilities => ["memory", "user", "preferences", "personal", "search", "recall"];

    static string[] IAgent.AgentRoutingExamples =>
        ["remember that I prefer", "save my preference", "recall what I like",
         "my favorite language is", "I always want", "don't forget that I"];

    static string IAgent.AgentInstructions =>
        "You are User Memory, the IAW team's long-term store for personal facts, preferences, and corrections. " +
        "Extract personal information from conversations and store it. Search and surface relevant memories when queried.";
}