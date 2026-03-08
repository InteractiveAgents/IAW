using Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

internal sealed class AgentTools(IClusterClient orleans)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string[] WellKnownAgentIds =
    [
        "personal-assistant", "roslyn", "dotnet", "nuget", "github",
        "reviewer", "self-improvement", "fs", "shell", "git",
        "build", "knowledge", "user", "planning", "notification"
    ];

    [McpServerTool(Name = "agent_list_all")]
    [Description("List all registered agents with their profile and capabilities.")]
    public async Task<string> AgentListAll(CancellationToken ct)
    {
        var results = new List<AgentProfile>();
        foreach (var id in WellKnownAgentIds)
        {
            var agent = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:id);
            var profile = await agent.GetProfileAsync(ct);
            results.Add(profile);
        }
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    [McpServerTool(Name = "assistant_chat")]
    [Description("Send a message to the PersonalAssistant and get a response.")]
    public async Task<string> AssistantChat(
        [Description("The message to send to the assistant")] string message,
        CancellationToken ct)
    {
        var assistant = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:"personal-assistant");
        var request = new AgentRequest { Input = message };
        var reply = await assistant.RespondAsync(request, ct);
        return JsonSerializer.Serialize(new { reply.Output, reply.ModelId, reply.TimestampUtc }, JsonOptions);
    }

    [McpServerTool(Name = "agent_send_message")]
    [Description("Send a message to any agent by ID and get a response.")]
    public async Task<string> AgentSendMessage(
        [Description("The agent grain ID (e.g. 'roslyn', 'shell', 'github')")] string agentId,
        [Description("The message to send")] string message,
        CancellationToken ct)
    {
        var agent = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:agentId);
        var request = new AgentRequest { Input = message };
        var reply = await agent.RespondAsync(request, ct);
        return JsonSerializer.Serialize(new { agentId, reply.Output, reply.ModelId, reply.TimestampUtc }, JsonOptions);
    }

    [McpServerTool(Name = "agent_get_status")]
    [Description("Get an agent's profile and recent activity.")]
    public async Task<string> AgentGetStatus(
        [Description("The agent grain ID")] string agentId,
        CancellationToken ct)
    {
        var agent = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:agentId);
        var profile = await agent.GetProfileAsync(ct);
        var recentMessages = await agent.QueryMessagesAsync(
            new AgentMessageQuery { Limit = 5, Descending = true }, ct);
        var schedule = await agent.GetScheduleStatusAsync(ct);
        return JsonSerializer.Serialize(new { profile, recentMessages, schedule }, JsonOptions);
    }

    [McpServerTool(Name = "agent_assign_task")]
    [Description("Assign a task to PersonalAssistant for delegation to the engineering team.")]
    public async Task<string> AgentAssignTask(
        [Description("Task description")] string task,
        [Description("Priority: low, medium, high")] string priority = "medium",
        CancellationToken ct = default)
    {
        var pa = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:"personal-assistant");
        var request = new AgentRequest
        {
            Input = task,
            Metadata = new() { ["priority"] = priority, ["type"] = "task" }
        };
        var reply = await pa.RespondAsync(request, ct);
        return JsonSerializer.Serialize(new { task, priority, reply.Output }, JsonOptions);
    }

    [McpServerTool(Name = "agent_get_events")]
    [Description("Get events from an agent's event log.")]
    public async Task<string> AgentGetEvents(
        [Description("The agent grain ID")] string agentId,
        [Description("Maximum number of events to return")] int limit = 20,
        CancellationToken ct = default)
    {
        var agent = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:agentId);
        var events = await agent.QueryEventsAsync(
            new AgentEventQuery { Limit = limit, Descending = true }, ct);
        return JsonSerializer.Serialize(events, JsonOptions);
    }

    [McpServerTool(Name = "agent_get_metrics")]
    [Description("Get agent performance metrics including message count, event count, and schedule status.")]
    public async Task<string> AgentGetMetrics(
        [Description("The agent grain ID")] string agentId,
        CancellationToken ct = default)
    {
        var agent = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:agentId);
        var profile = await agent.GetProfileAsync(ct);
        var messageCount = (await agent.QueryMessagesAsync(ct: ct)).Count;
        var eventCount = (await agent.QueryEventsAsync(ct: ct)).Count;
        var schedule = await agent.GetScheduleStatusAsync(ct);
        return JsonSerializer.Serialize(new
        {
            profile.Id, profile.DisplayName, messageCount, eventCount, schedule
        }, JsonOptions);
    }

    [McpServerTool(Name = "agent_trigger_self_improvement")]
    [Description("Trigger self-improvement analysis across the agent team.")]
    public async Task<string> AgentTriggerSelfImprovement(CancellationToken ct)
    {
        var agent = orleans.GetGrain<IAgent>(grainClassNamePrefix: "Samples.SmartAgent", primaryKey:"self-improvement");
        var request = new AgentRequest
        {
            Input = "Analyze recent agent interactions and propose improvements"
        };
        var reply = await agent.RespondAsync(request, ct);
        return JsonSerializer.Serialize(new { reply.Output, reply.TimestampUtc }, JsonOptions);
    }
}
