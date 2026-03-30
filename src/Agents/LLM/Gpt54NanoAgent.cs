using Core;
using Core.AI;
using Core.AI.Models.OpenAI;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt54NanoAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54Nano>] IChatClient chatClient)
    : LlmAgentBase<IGpt54Nano>(durableState, chatClient), IGpt54Nano;