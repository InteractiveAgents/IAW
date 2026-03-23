using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt4oAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt4o>] IChatClient chatClient)
    : LlmAgentBase<IGpt4o>(durableState, chatClient), IGpt4o;
