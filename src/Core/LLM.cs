using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace Core;

public abstract class LlmAgentBase<TContract>(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent<TContract>(durableState, chatClient) where TContract : IAgent
{
    protected override string Instructions =>
        $"You are {DisplayName}, an IAW team language model. Answer directly, accurately, and concisely.";
}
