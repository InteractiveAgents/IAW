using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Models;

public class Claude45HaikuAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IClaude45Haiku
{
    protected override string DisplayName => "Claude 4.5 Haiku";

    public static string AgentDescription => "Claude 4.5 Haiku fast and lightweight language model wrapper optimized for low-latency tasks.";
    public static string[] AgentCapabilities => ["llm", "reasoning", "generation", "claude", "anthropic", "fast"];
}
