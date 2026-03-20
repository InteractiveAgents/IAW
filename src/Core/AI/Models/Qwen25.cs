using Core.Contracts;

namespace Core.AI.Models;

public sealed class Qwen25 : LLMModel
{
    public override string Id => "qwen2.5";
    public override string DisplayName => "Qwen 2.5";
    public override string Provider => "ollama";
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}

public interface IQwen25 : IAgent
{
    static string IAgent.AgentDisplayName => "Qwen 2.5";
    static string IAgent.AgentDescription => "Qwen 2.5 language model wrapper from Alibaba for multilingual reasoning and generation.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "qwen", "alibaba", "multilingual"];
}
