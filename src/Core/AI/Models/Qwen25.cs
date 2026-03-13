using Core.Contracts;

namespace Core.AI.Models;

public sealed class Qwen25 : LLMModel
{
    public static readonly Qwen25 Instance = new();
    private Qwen25() { }

    public override string Id => "qwen2.5";
    public override string DisplayName => "Qwen 2.5";
    public override string Provider => "ollama";
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}

public interface IQwen25 : IAgent { }
