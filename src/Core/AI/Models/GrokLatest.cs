using Core.Contracts;

namespace Core.AI.Models;

public sealed class GrokLatest : LLMModel
{
    public override string Id => "grok-latest";
    public override string DisplayName => "Grok Latest";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGrokLatest : IAgent { }
