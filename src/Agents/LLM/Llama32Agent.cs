using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Llama32Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Llama32>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), ILlama32
{
    protected override string DisplayName => "Llama 3.2";

    public static string AgentDescription => "Llama 3.2 open-weight language model wrapper for local and on-premise inference.";
    public static string[] AgentCapabilities => ["llm", "reasoning", "generation", "llama", "meta", "local"];
}
