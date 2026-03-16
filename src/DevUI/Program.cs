using DevUI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using Aspire.IAW;

var builder = WebApplication.CreateBuilder(args);

builder.AddIAWClient();

builder.Services.AddSingleton<IChatClient, OrleansAgentChatClient>();

// Discover all IAgent grain interfaces from loaded assemblies and register with DevUI
AgentDiscovery.DiscoverAndRegisterAgents(builder);

builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();
app.UseHttpsRedirection();
app.MapDefaultEndpoints();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (builder.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.Run();
