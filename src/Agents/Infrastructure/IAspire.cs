using Core.Contracts;
using System.ComponentModel;

namespace IAW.Agents.Infrastructure;

public interface IAspire : IAgent
{
    static string IAgent.AgentDisplayName => "Aspire";

    static string IAgent.AgentDescription =>
        "Monitors and manages the running .NET Aspire application — resources, health, logs, traces, and telemetry via Aspire MCP tools.";

    static string[] IAgent.AgentCapabilities =>
        ["aspire", "health", "traces", "logs", "resources", "monitoring", "telemetry", "infrastructure", "status"];

    static string IAgent.AgentInstructions => """
        You are Aspire, the infrastructure and deployment operator. You manage the IAW
        distributed system through the Aspire dashboard.

        RULES:
        - For deploying CODE CHANGES after writing files: call Deploy (stops, rebuilds, starts fresh).
        - For simple service restarts (no code changes): call RestartResource.
        - When asked about system health: call ListResources and report states.
        - When asked about performance: call GetTraces and summarize token usage and timing.
        - For debugging: call GetLogs and surface errors/warnings first.
        - DO NOT execute shell commands for infrastructure tasks — use your typed tools.
        - DO NOT restart resources without being asked — deployments need explicit intent.

        TOOLS: Deploy, RestartResource, ListResources, GetTraces, GetLogs (typed interface methods).
        Additional MCP tools available for deeper queries.
        """;

    [Description("Restart an Aspire resource by name. Stops then starts the resource. Use to deploy code changes after a build. Common resources: assistant, telegram, devui, mcp.")]
    Task<string> RestartResourceAsync(string resourceName, CancellationToken ct = default);

    [Description("List all Aspire resources with their current state (Running, Stopped, Finished, etc).")]
    Task<string> ListResourcesAsync(CancellationToken ct = default);

    [Description("Get recent distributed traces for a resource. Shows operation names, durations, and token usage.")]
    Task<string> GetTracesAsync(string resourceName, CancellationToken ct = default);

    [Description("Get recent structured logs for a resource. Shows errors and warnings first.")]
    Task<string> GetLogsAsync(string resourceName, CancellationToken ct = default);

    [Description("Get recent logs for health monitoring.")]
    Task<string> GetHealthLogsAsync(string resourceName, CancellationToken ct = default);

    [Description("Build the solution then restart the assistant resource. Use after writing code changes. Fails fast if build has errors.")]
    Task<string> DeployAsync(CancellationToken ct = default);
}
