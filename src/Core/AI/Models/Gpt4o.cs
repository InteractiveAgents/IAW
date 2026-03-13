using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt4o : LLMModel
{
    public static readonly Gpt4o Instance = new();
    private Gpt4o() { }

    public override string Id => "gpt-4o";
    public override string DisplayName => "GPT-4o";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGpt4o : IAgent { }
