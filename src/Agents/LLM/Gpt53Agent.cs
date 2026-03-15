using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Gpt53Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt53>] IChatClient chatClient)
    : global::Core.LLM(durableState, chatClient), IGpt53
{
    protected override string DisplayName => "GPT 5.3";
}
