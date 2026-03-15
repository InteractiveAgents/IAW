using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Opus46Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Opus46>] IChatClient chatClient)
    : global::Core.LLM(durableState, chatClient), IOpus46
{
    protected override string DisplayName => "Claude Opus 4.6";
}
