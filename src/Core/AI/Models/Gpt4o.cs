using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt4o : LLMModel
{
    public override string Id => "gpt-4o";
    public override string DisplayName => "GPT-4o";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGpt4o : IAgent
{
    static string IAgent.AgentDisplayName => "GPT-4o";
    static string IAgent.AgentDescription => "GPT-4o language model wrapper for multimodal reasoning and general-purpose text generation.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "openai", "multimodal"];
}
