using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Claude45HaikuAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : global::Core.LLM(durableState, chatClient), IClaude45Haiku
{
    protected override string DisplayName => Claude45Haiku.Instance.DisplayName;
}
