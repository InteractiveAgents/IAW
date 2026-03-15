using Core.Contracts;

namespace Core.AI.Models;

public sealed class Claude45Haiku : LLMModel
{
    public override string Id => "claude-haiku-4-5-20251001";
    public override string DisplayName => "Claude 4.5 Haiku";
    public override string Provider => "anthropic";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IClaude45Haiku : IAgent { }
