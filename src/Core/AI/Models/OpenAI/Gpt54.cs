using Core.Contracts;

namespace Core.AI.Models.OpenAI;

public sealed class Gpt54 : LLMModel
{
    public override string Id => "gpt-5.4";
    public override string DisplayName => "GPT-5.4";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGpt54 : IAgent
{
    static string IAgent.AgentDisplayName => "GPT-5.4";
    static string IAgent.AgentDescription => "GPT-5.4 flagship language model wrapper for complex reasoning and high-quality generation.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "openai", "powerful"];
}
