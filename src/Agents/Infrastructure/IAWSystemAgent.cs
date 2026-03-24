using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace IAW.Agents.Infrastructure;

public class IAWSystemAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Opus46>] IChatClient chatClient,
    ILogger<IAWSystemAgent> logger)
    : Agent<IIAWSystem>(durableState, chatClient), IIAWSystem
{
    protected override IReadOnlyList<AITool> DefineAdditionalTools()
    {
        return [
            AIFunctionFactory.Create(SendToAgentAsync, "SendToAgent",
                "Send a task to a specialized agent. Available: Aspire, DotNet, FileSystem, Git, Roslyn, Shell.")
        ];
    }

    private async Task<string> SendToAgentAsync(string agentName, string request, CancellationToken ct = default)
    {
        logger.LogInformation("IAWSystem delegates to {Agent}: {Request}",
            agentName, request[..Math.Min(80, request.Length)]);

        var interfaceType = AgentInterfaceResolver.ResolveByDisplayName(agentName)
                         ?? AgentInterfaceResolver.Resolve(agentName);
        if (interfaceType is null)
            return $"Unknown agent: {agentName}. Available: Aspire, DotNet, FileSystem, Git, Roslyn, Shell.";

        var scope = this.GetPrimaryKeyString();
        var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{scope}/{interfaceType.Name}");

        try
        {
            var result = await agent.GetResponse(request, ct);
            return result.Length > 4000 ? result[..4000] + "\n...(truncated)" : result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IAWSystem: {Agent} failed", agentName);
            return $"Agent {agentName} failed: {ex.Message}";
        }
    }
}