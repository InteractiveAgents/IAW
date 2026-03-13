using Core.Contracts;

namespace Core.AI.Models;

public sealed class Opus46 : LLMModel
{
    public static readonly Opus46 Instance = new();
    private Opus46() { }

    public override string Id => "claude-opus-4-6";
    public override string DisplayName => "Claude Opus 4.6";
    public override string Provider => "anthropic";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IOpus46 : IAgent { }
