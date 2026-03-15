using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt52 : LLMModel
{
    public override string Id => "gpt-5.2";
    public override string DisplayName => "GPT 5.2";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGpt52 : IAgent { }
