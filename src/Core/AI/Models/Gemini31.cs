using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gemini31 : LLMModel
{
    public override string Id => "gemini-3.1";
    public override string DisplayName => "Gemini 3.1";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IGemini31 : IAgent { }
