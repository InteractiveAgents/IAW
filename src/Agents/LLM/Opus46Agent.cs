using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Opus46Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Opus46>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IOpus46
{
    protected override string DisplayName => "Claude Opus 4.6";
}
