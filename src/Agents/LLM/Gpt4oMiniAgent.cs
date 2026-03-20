using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt4oMiniAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt4oMini>] IChatClient chatClient)
    : LlmAgentBase<IGpt4oMini>(durableState, chatClient), IGpt4oMini;
