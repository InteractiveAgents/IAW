using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Llama32Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Llama32>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), ILlama32
{
    protected override string DisplayName => "Llama 3.2";
}
