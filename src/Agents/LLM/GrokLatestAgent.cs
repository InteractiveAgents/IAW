using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class GrokLatestAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<GrokLatest>] IChatClient chatClient)
    : LlmAgentBase<IGrokLatest>(durableState, chatClient), IGrokLatest;
