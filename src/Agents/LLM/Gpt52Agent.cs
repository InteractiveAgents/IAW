using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt52Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt52>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGpt52
{
    protected override string DisplayName => "GPT 5.2";
}
