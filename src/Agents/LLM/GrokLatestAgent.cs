using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class GrokLatestAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<GrokLatest>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGrokLatest
{
    protected override string DisplayName => "Grok Latest";
}
