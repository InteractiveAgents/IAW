using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Gpt54MiniAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54Mini>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGpt54Mini
{
    protected override string DisplayName => "GPT-5.4 Mini";
}
