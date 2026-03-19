using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt4oMiniAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt4oMini>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGpt4oMini
{
    protected override string DisplayName => "GPT-4o Mini";
}
