using System.Net;
using DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var gatewayAddress = builder.Configuration["Orleans:PrimaryGateway"];

builder.UseOrleansClient(client =>
{
    if (!string.IsNullOrEmpty(gatewayAddress))
    {
        var uri = new Uri(gatewayAddress);
        client.UseStaticClustering(new IPEndPoint(IPAddress.Loopback, uri.Port));
    }
    else
    {
        client.UseLocalhostClustering();
    }
});

builder.Services.AddSingleton<IChatClient, OrleansAgentChatClient>();

// Well-known agents — instructions field carries the grain ID for OrleansAgentChatClient routing
builder.AddAIAgent("personal-assistant", instructions: "personal-assistant");
builder.AddAIAgent("roslyn", instructions: "roslyn");
builder.AddAIAgent("dotnet", instructions: "dotnet");
builder.AddAIAgent("nuget", instructions: "nuget");
builder.AddAIAgent("github", instructions: "github");
builder.AddAIAgent("reviewer", instructions: "reviewer");
builder.AddAIAgent("self-improvement", instructions: "self-improvement");
builder.AddAIAgent("fs", instructions: "fs");
builder.AddAIAgent("shell", instructions: "shell");
builder.AddAIAgent("git", instructions: "git");
builder.AddAIAgent("build", instructions: "build");
builder.AddAIAgent("knowledge", instructions: "knowledge");
builder.AddAIAgent("user", instructions: "user");
builder.AddAIAgent("planning", instructions: "planning");
builder.AddAIAgent("notification", instructions: "notification");

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
