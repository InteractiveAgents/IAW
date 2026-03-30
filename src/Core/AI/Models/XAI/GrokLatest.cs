using Core.Contracts;

namespace Core.AI.Models.XAI;

public sealed class GrokLatest : LLMModel
{
    public override string Id => "grok-latest";
    public override string DisplayName => "Grok Latest";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGrokLatest : IAgent
{
    static string IAgent.AgentDisplayName => "Grok Latest";
    static string IAgent.AgentDescription => "Grok Latest language model wrapper from xAI for reasoning and conversational tasks.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "grok", "xai"];
}
