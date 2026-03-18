using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt54Nano : LLMModel
{
    public override string Id => "gpt-5.4-nano";
    public override string DisplayName => "GPT-5.4 Nano";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}

public interface IGpt54Nano : IAgent { }
