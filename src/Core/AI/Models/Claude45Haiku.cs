using Core.Contracts;

namespace Core.AI.Models;

public sealed class Claude45Haiku : LLMModel
{
    public override string Id => "claude-haiku-4-5-20251001";
    public override string DisplayName => "Claude 4.5 Haiku";
    public override string Provider => "anthropic";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IClaude45Haiku : IAgent
{
    static string IAgent.AgentDisplayName => "Claude 4.5 Haiku";
    static string IAgent.AgentDescription => "Claude 4.5 Haiku fast and lightweight language model wrapper optimized for low-latency tasks.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "claude", "anthropic", "fast"];
}
