using Core.Contracts;

namespace Core.AI.Models;

public sealed class Llama32 : LLMModel
{
    public static readonly Llama32 Instance = new();
    private Llama32() { }

    public override string Id => "llama3.2";
    public override string DisplayName => "Llama 3.2";
    public override ProviderType Provider => ProviderType.Ollama;
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}

public interface ILlama32 : IAgent { }
