using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Qwen25Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Qwen25>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IQwen25
{
    protected override string DisplayName => "Qwen 2.5";
}
