using System.ComponentModel;
using System.Text;
using System.Text.Json;
using IAW.Core;
using IAW.Core.AI;
using IAW.Core.AI.Models;
using IAW.Core.Orchestration;
using IAW.Core.Registry;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Orchestration;

public class PlanningAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<IAW.Core.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    IGrainFactory grainFactory)
    : Agent(state, eventLog, chatClient, history, trackingItems), IPlanning
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
        You are an orchestration engine for a multi-agent C# development system.
        Your job is to break down user requests into concrete steps that available agents can execute.

        You have access to tools for:
        - Querying the agent registry to discover available agents and their capabilities
        - Generating execution plans with specific steps, agents, and parameters
        - Executing plans as single-file C# apps that connect to the cluster

        Workflow:
        1. When a user makes a request, first query available agents to understand capabilities
        2. Design a plan with ordered steps, assigning each to the most appropriate agent
        3. Present the plan to the user for confirmation
        4. Once confirmed, generate and execute the plan

        Be specific in your plans. Each step should name the exact agent, the action,
        and all required parameters (workspace paths, file paths, descriptions).
        Never create vague steps like "do something" -- be precise.

        If a request can't be fulfilled with available agents, explain what's missing
        and suggest alternatives.
        """;
}
