using Core.AI;
using Core.Contracts;
using IAW.Agents.Orchestration;
using Orleans.Dashboard;
using Orleans.Journaling;
using ServiceDefaults;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var siloPort = builder.Configuration.GetValue("Orleans:Endpoints:SiloPort", 11_111);
var gatewayPort = builder.Configuration.GetValue("Orleans:Endpoints:GatewayPort", 30_000);
var clusterId = builder.Configuration.GetValue("Orleans:ClusterId", "dev");
var serviceId = builder.Configuration.GetValue("Orleans:ServiceId", "dev");

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering(
        siloPort: siloPort,
        gatewayPort: gatewayPort,
        serviceId: serviceId,
        clusterId: clusterId);
    silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
    silo.AddStateMachineStorage();
    silo.AddDashboard();
});

builder.AddLlmProviders();
builder.AddServiceDefaults();
var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");

app.MapGet("/", () => "Hello World!");

app.MapGet("/samples/github-models", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<Samples.IGitHubTestAgent>($"github-test-{Guid.NewGuid():N}");
    var metadata = await agent.GetMetadata(ct);

    return Results.Ok(new
    {
        model = "gpt-4o-mini",
        provider = "GitHub",
        agentType = metadata.AgentType,
        displayName = metadata.DisplayName
    });
});

app.MapGet("/samples/agent/chat", async (
    IGrainFactory grains,
    string? agentId,
    string? prompt,
    CancellationToken ct) =>
{
    var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? $"sample-chat-{Guid.NewGuid():N}" : agentId;
    var resolvedPrompt = string.IsNullOrWhiteSpace(prompt) ? "Hello, what can you do?" : prompt;
    var agent = grains.GetGrain<IPersonalAssistant>(resolvedAgentId);

    var response = await agent.GetResponse(resolvedPrompt, ct);
    var historySnapshot = await agent.GetHistory(ct);

    return Results.Ok(new
    {
        agentId = resolvedAgentId,
        prompt = resolvedPrompt,
        response,
        historyCount = historySnapshot.Count
    });
});

app.MapGet("/samples/agent/metadata", async (
    IGrainFactory grains,
    string? agentId,
    CancellationToken ct) =>
{
    var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? $"sample-meta-{Guid.NewGuid():N}" : agentId;
    var agent = grains.GetGrain<IPersonalAssistant>(resolvedAgentId);
    var metadata = await agent.GetMetadata(ct);
    return Results.Ok(metadata);
});

app.MapGet("/samples/agent/capabilities", async (
    IGrainFactory grains,
    string? agentId,
    CancellationToken ct) =>
{
    var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? $"sample-cap-{Guid.NewGuid():N}" : agentId;
    var agent = grains.GetGrain<IPersonalAssistant>(resolvedAgentId);
    var capabilities = await agent.GetCapabilities(ct);
    return Results.Ok(capabilities);
});

app.MapGet("/samples/agent/events", async (
    IGrainFactory grains,
    CancellationToken ct) =>
{
    var agentId = $"sample-events-{Guid.NewGuid():N}";
    var agent = grains.GetGrain<IPersonalAssistant>(agentId);

    // trigger events via conversation (agents publish internally)
    await agent.GetResponse("Log a weather event for Seattle.", ct);

    var eventLog = await agent.GetEventLog(ct);

    return Results.Ok(new
    {
        agentId,
        count = eventLog.Count,
        eventNames = eventLog.Select(e => e.EventName).ToArray()
    });
});

app.MapGet("/samples/agent/state", async (
    IGrainFactory grains,
    string? workspace,
    CancellationToken ct) =>
{
    var agentId = $"sample-state-{Guid.NewGuid():N}";
    var resolvedWorkspace = string.IsNullOrWhiteSpace(workspace) ? "/tmp/sample-workspace" : workspace;
    var agent = grains.GetGrain<IPersonalAssistant>(agentId);

    await agent.SetWorkspace(resolvedWorkspace, ct);
    var agentState = await agent.GetState(ct);

    return Results.Ok(new
    {
        agentId,
        workspace = resolvedWorkspace,
        entryCount = agentState.Entries.Count,
        entries = agentState.Entries.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Value?.ToString() ?? "")
    });
});

app.MapGet("/samples/agent/stream", async (
    IGrainFactory grains,
    string? agentId,
    string? prompt,
    CancellationToken ct) =>
{
    var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? $"sample-stream-{Guid.NewGuid():N}" : agentId;
    var resolvedPrompt = string.IsNullOrWhiteSpace(prompt) ? "Count from 1 to 5." : prompt;
    var agent = grains.GetGrain<IPersonalAssistant>(resolvedAgentId);

    var chunks = new StringBuilder();
    var chunkCount = 0;

    await foreach (var chunk in agent.GetResponseStream(resolvedPrompt, ct))
    {
        chunks.Append(chunk);
        chunkCount++;
    }

    return Results.Ok(new
    {
        agentId = resolvedAgentId,
        prompt = resolvedPrompt,
        response = chunks.ToString(),
        chunkCount,
        streamed = chunkCount > 0
    });
});

app.MapGet("/samples/agent/subscriptions", async (
    IGrainFactory grains,
    string? agentId,
    CancellationToken ct) =>
{
    var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? $"sample-subs-{Guid.NewGuid():N}" : agentId;
    var agent = grains.GetGrain<IPersonalAssistant>(resolvedAgentId);

    var subscriptions = await agent.GetActiveSubscriptions(ct);

    return Results.Ok(new
    {
        agentId = resolvedAgentId,
        count = subscriptions.Count,
        subscriptions
    });
});

app.MapGet("/samples/agent/history", async (
    IGrainFactory grains,
    string? agentId,
    CancellationToken ct) =>
{
    var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? $"sample-history-{Guid.NewGuid():N}" : agentId;
    var agent = grains.GetGrain<IPersonalAssistant>(resolvedAgentId);

    await agent.GetResponse("Hello!", ct);
    await agent.GetResponse("How are you?", ct);

    var historyBefore = await agent.GetHistory(ct);
    var countBefore = historyBefore.Count;

    await agent.ClearHistory(ct);
    var historyAfter = await agent.GetHistory(ct);

    return Results.Ok(new
    {
        agentId = resolvedAgentId,
        historyCountBeforeClear = countBefore,
        historyCountAfterClear = historyAfter.Count,
        cleared = historyAfter.Count == 0
    });
});

app.MapGet("/samples/agent/cancel", async (
    IGrainFactory grains,
    CancellationToken ct) =>
{
    var agentId = $"sample-cancel-{Guid.NewGuid():N}";
    var agent = grains.GetGrain<IPersonalAssistant>(agentId);

    await agent.Cancel(ct);

    return Results.Ok(new
    {
        agentId,
        cancelled = true
    });
});

app.MapGet("/samples/agent/assign-task", async (
    IGrainFactory grains,
    CancellationToken ct) =>
{
    var agentId = $"sample-task-{Guid.NewGuid():N}";
    var agent = grains.GetGrain<IPersonalAssistant>(agentId);

    var response = await agent.GetResponse("Review pull request #42 with high priority.", ct);
    var eventLog = await agent.GetEventLog(ct);

    return Results.Ok(new
    {
        agentId,
        response,
        eventLogCount = eventLog.Count
    });
});

app.Run();
