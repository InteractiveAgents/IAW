using Core.Contracts;

namespace Core.AI.Models.Ollama;

public sealed class Qwen25_7B : LLMModel
{
    public override string Id => "qwen2.5:7b";
    public override string DisplayName => "Qwen 2.5 7B";
    public override string Provider => "ollama";
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}

public interface IQwen25_7B : IAgent
{
    static string IAgent.AgentDisplayName => "Qwen 2.5 7B";
    static string IAgent.AgentDescription => "Qwen 2.5 7B parameter model for local inference on consumer GPUs (8GB+ VRAM).";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "qwen", "alibaba", "multilingual", "local"];
}
