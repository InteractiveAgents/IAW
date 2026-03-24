using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt53Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt53>] IChatClient chatClient)
    : LlmAgentBase<IGpt53>(durableState, chatClient), IGpt53;