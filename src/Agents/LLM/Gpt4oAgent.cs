using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Gpt4oAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt4o>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IGpt4o
{
    protected override string DisplayName => "GPT-4o";

    public static string AgentDescription => "GPT-4o language model wrapper for multimodal reasoning and general-purpose text generation.";
    public static string[] AgentCapabilities => ["llm", "reasoning", "generation", "openai", "multimodal"];
}
