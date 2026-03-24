using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt54Mini : LLMModel
{
    public override string Id => "gpt-5.4-mini";
    public override string DisplayName => "GPT-5.4 Mini";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGpt54Mini : IAgent
{
    static string IAgent.AgentDisplayName => "GPT-5.4 Mini";
    static string IAgent.AgentDescription => "GPT-5.4 Mini compact language model wrapper offering high capability with reduced latency.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "openai", "fast"];
}