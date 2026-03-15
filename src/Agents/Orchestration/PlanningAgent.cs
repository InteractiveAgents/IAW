using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Orchestration;
using Core.Registry;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Orchestration;

public class PlanningAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    IGrainFactory grainFactory)
    : Agent(durableState, chatClient), IPlanning
{
    protected override string DisplayName => "Orchestrator";
    protected override string Instructions => PlanningPrompts.System;
    protected override AgentKind AgentKindValue => AgentKind.Dynamic;

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(QueryAgentsAsync),
            AIFunctionFactory.Create(GeneratePlanAsync),
            AIFunctionFactory.Create(ExecutePlanAsync)
        ];
    }

    [Description("Query the agent registry for available agents and their capabilities")]
    private async Task<string> QueryAgentsAsync()
    {
        var registry = grainFactory.GetGrain<IAgentRegistryGrain>("global");
        var agents = await registry.GetAllAsync();

        if (agents.Count == 0)
            return "No agents registered.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {agents.Count} registered agent(s):");
        foreach (var agent in agents)
        {
            sb.AppendLine($"- {agent.AgentType}: {agent.DisplayName}");
            if (!string.IsNullOrWhiteSpace(agent.Description))
                sb.AppendLine($"  Description: {agent.Description}");
            if (agent.Publishes.Length > 0)
                sb.AppendLine($"  Publishes: {string.Join(", ", agent.Publishes)}");
            if (agent.Subscribes.Length > 0)
                sb.AppendLine($"  Subscribes: {string.Join(", ", agent.Subscribes)}");
        }
        return sb.ToString();
    }

    [Description("Generate an orchestration plan from a description. Returns a JSON plan with ordered steps.")]
    private Task<string> GeneratePlanAsync(
        [Description("Summary of what the plan should accomplish")] string summary,
        [Description("JSON array of steps: [{order, agentType, action, parameters: {key: value}}]")] string stepsJson)
    {
        try
        {
            var steps = JsonSerializer.Deserialize<List<PlanStep>>(stepsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? [];

            var plan = new OrchestrationPlan(summary, steps);
            State["current-plan"] = new StateEntry("current-plan",
                JsonSerializer.Serialize(plan));

            return Task.FromResult($"Plan created: {summary} ({steps.Count} steps)");
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"Invalid steps JSON: {ex.Message}");
        }
    }

    [Description("Execute the current orchestration plan by generating and running a C# script")]
    private async Task<string> ExecutePlanAsync(
        [Description("Cluster endpoint (e.g. localhost)")] string endpoint = "localhost",
        [Description("Gateway port")] int gatewayPort = 30000)
    {
        if (!State.TryGetValue("current-plan", out var planDesc))
            return "No plan available. Generate a plan first.";

        try
        {
            var plan = JsonSerializer.Deserialize<OrchestrationPlan>(planDesc.Value.ToString()!);
            if (plan is null)
                return "Failed to deserialize plan.";

            var script = ScriptGenerator.Generate(plan, endpoint, gatewayPort);
            var workspace = GetWorkspacePath() ?? Path.GetTempPath();
            var executor = new ScriptExecutor();
            var result = await executor.ExecuteScriptAsync(script, workspace);

            State["last-execution-result"] = new StateEntry("last-execution-result", result.Output);
            State["last-execution-success"] = new StateEntry("last-execution-success", result.Success);
            await WriteStateAsync();

            return result.Success
                ? $"Execution succeeded:\n{TrimOutput(result.Output)}"
                : $"Execution failed (exit code {result.ExitCode}):\n{TrimOutput(result.Output)}";
        }
        catch (Exception ex)
        {
            return $"Execution error: {ex.Message}";
        }
    }

    private static string TrimOutput(string output, int maxLength = 4000)
        => output.Length <= maxLength ? output : output[..maxLength] + "\n... (truncated)";
}

internal static class PlanningPrompts
{
    public const string System = """
        You are the Orchestrator, the IAW team's planning and execution engine. Decompose requests into multi-step plans and coordinate agent execution.

        CAPABILITIES:
        - Query the agent registry to discover available agents and their capabilities
        - Generate multi-step execution plans from user requests
        - Execute plans by invoking agents in sequence with parameter passing
        - Handle step failures with retry or skip decisions
        - Report execution progress and final outcomes

        PLAN FORMAT:
        Each plan step must specify: agent key, action description, expected output, dependencies on prior steps
        Plans should be minimal (fewest steps to achieve the goal).
        Break complex tasks into independent steps where possible to enable future parallelism.

        WORKFLOW:
        1. Before generating a plan, query available agents to verify capabilities
        2. Design a plan with ordered steps; each naming the exact agent, action, and all required parameters
        3. Present the plan and execute it
        4. Report execution progress: "Step 2/5: Building project... OK (3.2s)"

        RULES:
        - Never generate plans with more than 10 steps; break into sub-plans instead
        - Be precise: every step must specify agent, action, and all required parameters
        - No vague steps like "use the build agent"; say "Run Build.BuildAsync(projectPath, Release)"
        - If a request can't be fulfilled, explain what's missing or what agents are unavailable
        """;
}
