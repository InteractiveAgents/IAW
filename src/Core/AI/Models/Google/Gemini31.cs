using Core.Contracts;

namespace Core.AI.Models.Google;

public sealed class Gemini31 : LLMModel
{
    public override string Id => "gemini-3.1";
    public override string DisplayName => "Gemini 3.1";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGemini31 : IAgent
{
    static string IAgent.AgentDisplayName => "Gemini 3.1";
    static string IAgent.AgentDescription => "Gemini 3.1 language model wrapper from Google for multimodal reasoning and generation.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "gemini", "google", "multimodal"];
}
