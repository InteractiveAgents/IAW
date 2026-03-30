using Core;
using Core.AI;
using Core.AI.Models.OpenAI;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt54Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54>] IChatClient chatClient)
    : LlmAgentBase<IGpt54>(durableState, chatClient), IGpt54;
