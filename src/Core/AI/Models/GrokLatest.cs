using Core.Contracts;

namespace Core.AI.Models;

public sealed class GrokLatest : LLMModel
{
    public static readonly GrokLatest Instance = new();
    private GrokLatest() { }

    public override string Id => "grok-latest";
    public override string DisplayName => "Grok Latest";
    public override ProviderType Provider => ProviderType.OpenAI;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGrokLatest : IAgent { }
