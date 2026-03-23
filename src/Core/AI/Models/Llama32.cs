using Core.Contracts;

namespace Core.AI.Models;

public sealed class Llama32 : LLMModel
{
    public override string Id => "llama3.2";
    public override string DisplayName => "Llama 3.2";
    public override string Provider => "ollama";
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}

public interface ILlama32 : IAgent
{
    static string IAgent.AgentDisplayName => "Llama 3.2";
    static string IAgent.AgentDescription => "Llama 3.2 open-weight language model wrapper for local and on-premise inference.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "llama", "meta", "local"];
}
