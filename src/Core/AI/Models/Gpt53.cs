using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt53 : LLMModel
{
    public static readonly Gpt53 Instance = new();
    private Gpt53() { }

    public override string Id => "gpt-5.3";
    public override string DisplayName => "GPT 5.3";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGpt53 : IAgent { }
