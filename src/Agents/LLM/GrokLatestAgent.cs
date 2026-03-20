using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class GrokLatestAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<GrokLatest>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGrokLatest
{
    protected override string DisplayName => "Grok Latest";

    public static string AgentDescription => "Grok Latest language model wrapper from xAI for reasoning and conversational tasks.";
    public static string[] AgentCapabilities => ["llm", "reasoning", "generation", "grok", "xai"];
}
