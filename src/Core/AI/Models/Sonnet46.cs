using Core.Contracts;

namespace Core.AI.Models;

public sealed class Sonnet46 : LLMModel
{
    public override string Id => "claude-sonnet-4-6";
    public override string DisplayName => "Claude Sonnet 4.6";
    public override string Provider => "anthropic";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface ISonnet46 : IAgent { }
