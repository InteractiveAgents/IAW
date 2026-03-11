using Core.Contracts;

namespace Core.AI.Models;

public sealed class Sonnet46 : LLMModel
{
    public static readonly Sonnet46 Instance = new();
    private Sonnet46() { }

    public override string Id => "claude-sonnet-4-6";
    public override string DisplayName => "Claude Sonnet 4.6";
    public override ProviderType Provider => ProviderType.Anthropic;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface ISonnet46 : IAgent { }
