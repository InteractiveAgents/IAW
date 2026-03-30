using Core;
using Core.AI;
using Core.AI.Models.Ollama;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Qwen25Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Qwen25>] IChatClient chatClient)
    : LlmAgentBase<IQwen25>(durableState, chatClient), IQwen25;