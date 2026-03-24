using Core.Contracts;

namespace Core.AI.Models;

public sealed class Sonnet46 : LLMModel
{
    public override string Id => "claude-sonnet-4-6";
    public override string DisplayName => "Claude Sonnet 4.6";
    public override string Provider => "anthropic";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface ISonnet46 : IAgent
{
    static string IAgent.AgentDisplayName => "Claude Sonnet 4.6";
    static string IAgent.AgentDescription => "Claude Sonnet 4.6 language model wrapper for general-purpose reasoning and text generation.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "claude", "anthropic"];
}