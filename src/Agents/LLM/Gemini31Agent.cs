using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Gemini31Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gemini31>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGemini31
{
    protected override string DisplayName => "Gemini 3.1";
}
