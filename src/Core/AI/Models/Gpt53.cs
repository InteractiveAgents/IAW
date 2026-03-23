using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt53 : LLMModel
{
    public override string Id => "gpt-5.3";
    public override string DisplayName => "GPT 5.3";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGpt53 : IAgent
{
    static string IAgent.AgentDisplayName => "GPT 5.3";
    static string IAgent.AgentDescription => "GPT 5.3 language model wrapper for advanced reasoning and complex task completion.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "openai"];
}