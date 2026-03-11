using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt4oMini : LLMModel
{
    public static readonly Gpt4oMini Instance = new();
    private Gpt4oMini() { }

    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GPT-4o Mini";
    public override ProviderType Provider => ProviderType.OpenAI;
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}

public interface IGpt4oMini : IAgent { }
