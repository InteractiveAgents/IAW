using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt54MiniAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54Mini>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGpt54Mini
{
    protected override string DisplayName => "GPT-5.4 Mini";

    public static string AgentDescription => "GPT-5.4 Mini compact language model wrapper offering high capability with reduced latency.";
    public static string[] AgentCapabilities => ["llm", "reasoning", "generation", "openai", "fast"];
}
