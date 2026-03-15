using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt4oMini : LLMModel
{
    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GPT-4o Mini";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}

public interface IGpt4oMini : IAgent { }
