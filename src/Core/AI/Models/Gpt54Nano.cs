using Core.Contracts;

namespace Core.AI.Models;

public sealed class Gpt54Nano : LLMModel
{
    public override string Id => "gpt-5.4-nano";
    public override string DisplayName => "GPT-5.4 Nano";
    public override string Provider => "openai";
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}

public interface IGpt54Nano : IAgent
{
    static string IAgent.AgentDisplayName => "GPT-5.4 Nano";
    static string IAgent.AgentDescription => "GPT-5.4 Nano ultra-lightweight language model wrapper for minimal-latency inference.";
    static string[] IAgent.AgentCapabilities => ["llm", "reasoning", "generation", "openai", "fast", "nano"];
}
