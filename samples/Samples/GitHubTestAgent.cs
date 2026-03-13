using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace Samples;

public interface IGitHubTestAgent : IAgent;

public sealed class GitHubTestAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<GitHubGpt4oMini>] IChatClient chatClient)
    : Agent(durableState, chatClient), IGitHubTestAgent
{
    protected override string Instructions => "You are a helpful test agent. Keep responses under 50 words.";
    protected override string DisplayName => "GitHub Test Agent";
}
