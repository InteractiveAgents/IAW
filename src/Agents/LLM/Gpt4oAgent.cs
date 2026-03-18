using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Gpt4oAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt4o>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGpt4o
{
    protected override string DisplayName => "GPT-4o";
}
