using Aspire.IAW;
using DevUI;
using DevUI.Visualization;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.AddIAWClient();

builder.Services.AddSingleton<IChatClient, OrleansAgentChatClient>();

// Discover all IAgent grain interfaces from loaded assemblies and register with DevUI
var agentRefs = AgentDiscovery.DiscoverAndRegisterAgents(builder);

builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();
builder.AddOpenAIChatCompletions();

builder.Services.AddSignalR();
builder.Services.AddHostedService<AgentEventForwarder>();

var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapDefaultEndpoints();

// Per-agent OpenAI-compatible endpoints (GA pattern)
foreach (var agentRef in agentRefs)
{
    app.MapOpenAIResponses(agentRef);
    app.MapOpenAIChatCompletions(agentRef);
}

app.MapOpenAIConversations();
app.MapHub<AgentVisualizationHub>("/visualization/hub");

if (builder.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.Run();
