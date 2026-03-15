using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Sonnet46Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : global::Core.LLM(durableState, chatClient), ISonnet46
{
    protected override string DisplayName => "Claude Sonnet 4.6";
}
