using Core.Contracts;
using Microsoft.Extensions.AI;

namespace Core;

public abstract class LLM(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : IAW.Core.Agent(durableState, chatClient)
{
    protected override string Instructions =>
        $"You are {DisplayName}, an IAW team language model. Answer directly, accurately, and concisely.";
}
