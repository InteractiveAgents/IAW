using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Core;

public abstract class Agent<TContract>(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient) where TContract : IAgent
{
    protected override string DisplayName => TContract.AgentDisplayName;
    protected override string Instructions => TContract.AgentInstructions;
}