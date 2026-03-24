using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gemini31Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gemini31>] IChatClient chatClient)
    : LlmAgentBase<IGemini31>(durableState, chatClient), IGemini31;