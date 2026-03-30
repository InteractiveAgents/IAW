using Core.Contracts;

namespace Core.AI.Models.Anthropic;

public sealed class Opus46 : LLMModel
{
    public override string Id => "claude-opus-4-6";
    public override string DisplayName => "Claude Opus 4.6";
    public override string Provider => "anthropic";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IOpus46 : IAgent
{
    static string IAgent.AgentDisplayName => "Claude Opus 4.6";
    static string IAgent.AgentDescription => "Claude Opus 4.6 most capable Anthropic model wrapper for complex reasoning and nuanced analysis.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "claude", "anthropic", "powerful"];
}
