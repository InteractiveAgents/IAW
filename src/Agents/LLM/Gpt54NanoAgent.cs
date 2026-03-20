using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt54NanoAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54Nano>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGpt54Nano
{
    protected override string DisplayName => "GPT-5.4 Nano";

    public static string AgentDescription => "GPT-5.4 Nano ultra-lightweight language model wrapper for minimal-latency inference.";
    public static string[] AgentCapabilities => ["llm", "reasoning", "generation", "openai", "fast", "nano"];
}
