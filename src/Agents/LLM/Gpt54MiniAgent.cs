using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt54MiniAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54Mini>] IChatClient chatClient)
    : LlmAgentBase<IGpt54Mini>(durableState, chatClient), IGpt54Mini;