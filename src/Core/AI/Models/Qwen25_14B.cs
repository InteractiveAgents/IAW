using Core.Contracts;

namespace Core.AI.Models;

public sealed class Qwen25_14B : LLMModel
{
    public override string Id => "qwen2.5:14b";
    public override string DisplayName => "Qwen 2.5 14B";
    public override string Provider => "ollama";
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}

public interface IQwen25_14B : IAgent
{
    static string IAgent.AgentDisplayName => "Qwen 2.5 14B";
    static string IAgent.AgentDescription => "Qwen 2.5 14B parameter model for local inference on high-VRAM GPUs (16GB+).";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "qwen", "alibaba", "multilingual", "local"];
}
