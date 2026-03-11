using System.Text.Json;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Core.Contracts;
using Core.Orchestration;

internal sealed class AgentTools(IClusterClient orleans)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private IAgent ResolveAgent(string agentId)
    {
        var entry = InterfaceCatalog.Discover()
            .FirstOrDefault(e => string.Equals(e.GrainId, agentId, StringComparison.OrdinalIgnoreCase));

        if (entry is not null)
            return (IAgent)orleans.GetGrain(entry.InterfaceType, agentId);

        if (agentId.StartsWith("dynamic-"))
            return orleans.GetGrain<IDynamicAgent>(agentId);

        var known = string.Join(", ", InterfaceCatalog.Discover().Select(e => e.GrainId));
        throw new ArgumentException($"Unknown agent ID: {agentId}. Known: {known}");
    }

    [McpServerTool(Name = "agent_list_all")]
    [Description("List all registered agents with their metadata and capabilities.")]
    public async Task<string> AgentListAll(CancellationToken ct)
    {
        var catalog = InterfaceCatalog.Discover();
        var results = new List<object>();
        foreach (var entry in catalog)
        {
            try
            {
                var agent = ResolveAgent(entry.GrainId);
                var metadata = await agent.GetMetadata(ct);
                var capabilities = await agent.GetCapabilities(ct);
                results.Add(new
                {
                    id = entry.GrainId,
                    interfaceName = entry.InterfaceName,
                    produces = entry.Produces,
                    consumes = entry.Consumes,
                    receives = entry.Receives,
                    metadata,
                    capabilities
                });
            }
            catch
            {
                results.Add(new { id = entry.GrainId, error = "Agent not available" });
            }
        }
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "assistant_chat")]
    [Description("Send a message to the PersonalAssistant and get a response.")]
    public async Task<string> AssistantChat(
        [Description("The message to send to the assistant")] string message,
        CancellationToken ct)
    {
        var assistant = ResolveAgent("personal-assistant");
        var response = await assistant.GetResponse(message, ct);
        return JsonSerializer.Serialize(new { agentId = "personal-assistant", response }, JsonOptions);
    }

    [McpServerTool(Name = "agent_send_message")]
    [Description("Send a message to any agent by ID and get a response.")]
    public async Task<string> AgentSendMessage(
        [Description("The agent grain ID (e.g. 'roslyn', 'shell', 'git-hub')")] string agentId,
        [Description("The message to send")] string message,
        CancellationToken ct)
    {
        var agent = ResolveAgent(agentId);
        var response = await agent.GetResponse(message, ct);
        return JsonSerializer.Serialize(new { agentId, response }, JsonOptions);
    }

    [McpServerTool(Name = "agent_get_status")]
    [Description("Get an agent's metadata, capabilities, and recent events.")]
    public async Task<string> AgentGetStatus(
        [Description("The agent grain ID")] string agentId,
        CancellationToken ct)
    {
        var agent = ResolveAgent(agentId);
        var metadata = await agent.GetMetadata(ct);
        var capabilities = await agent.GetCapabilities(ct);
        var allEvents = await agent.GetEventLog(ct);
        var recentEvents = allEvents.TakeLast(5).ToList();
        return JsonSerializer.Serialize(new { metadata, capabilities, recentEvents }, JsonOptions);
    }

    [McpServerTool(Name = "agent_assign_task")]
    [Description("Assign a task to PersonalAssistant for delegation to the engineering team.")]
    public async Task<string> AgentAssignTask(
        [Description("Task description")] string task,
        [Description("Priority: low, medium, high")] string priority = "medium",
        CancellationToken ct = default)
    {
        var assistant = ResolveAgent("personal-assistant");
        var prompt = $"[TASK] Priority: {priority}\n\n{task}";
        var response = await assistant.GetResponse(prompt, ct);
        return JsonSerializer.Serialize(new { task, priority, response }, JsonOptions);
    }

    [McpServerTool(Name = "agent_get_events")]
    [Description("Get events from an agent's event log.")]
    public async Task<string> AgentGetEvents(
        [Description("The agent grain ID")] string agentId,
        [Description("Maximum number of events to return")] int limit = 20,
        CancellationToken ct = default)
    {
        var agent = ResolveAgent(agentId);
        var allEvents = await agent.GetEventLog(ct);
        var events = allEvents.TakeLast(limit).ToList();
        return JsonSerializer.Serialize(events, JsonOptions);
    }

    [McpServerTool(Name = "agent_get_metrics")]
    [Description("Get agent performance metrics including metadata, event count, history count, and capabilities.")]
    public async Task<string> AgentGetMetrics(
        [Description("The agent grain ID")] string agentId,
        CancellationToken ct = default)
    {
        var agent = ResolveAgent(agentId);
        var metadata = await agent.GetMetadata(ct);
        var capabilities = await agent.GetCapabilities(ct);
        var eventCount = (await agent.GetEventLog(ct)).Count;
        var historyCount = (await agent.GetHistory(ct)).Count;
        return JsonSerializer.Serialize(new
        {
            metadata.AgentType, metadata.DisplayName, metadata.Description,
            eventCount, historyCount, capabilities
        }, JsonOptions);
    }

    [McpServerTool(Name = "agent_trigger_self_improvement")]
    [Description("Trigger self-improvement analysis across the agent team.")]
    public async Task<string> AgentTriggerSelfImprovement(CancellationToken ct)
    {
        var agent = ResolveAgent("self-improvement");
        var response = await agent.GetResponse(
            "Analyze recent agent interactions and propose improvements", ct);
        return JsonSerializer.Serialize(new { response }, JsonOptions);
    }
}
