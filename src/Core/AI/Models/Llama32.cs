using Core.Contracts;

namespace Core.AI.Models;

public sealed class Llama32 : LLMModel
{
    public override string Id => "llama3.2";
    public override string DisplayName => "Llama 3.2";
    public override string Provider => "ollama";
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}

public interface ILlama32 : IAgent { }
