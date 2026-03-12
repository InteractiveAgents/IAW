using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());

// Production silo — hosts all agents, memory, LLM
var assistant = builder.AddProject<Projects.IAW_Assistant>("assistant")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30000; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11111; })
    .WithUrlForEndpoint("https", ep => new()
    {
        Url = "/dashboard",
        DisplayText = "Orleans Dashboard"
    });

// Demo silo — independent, no clients depend on it
builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30002; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11113; });

// Clients — all connect to assistant gateway
builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder)
    .WithEnvironment("Orleans__PrimaryGateway", assistant.GetEndpoint("orleans-gateway"))
    .WaitFor(assistant);

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Orleans__PrimaryGateway", assistant.GetEndpoint("orleans-gateway"))
    .WithHttpEndpoint(port: 5300, name: "mcp-direct", isProxied: false)
    .WaitFor(assistant);

// Telegram client
var botToken = builder.AddParameter("bot-token", secret: true);
builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Orleans__PrimaryGateway", assistant.GetEndpoint("orleans-gateway"))
    .WithEnvironment("Telegram__BotToken", botToken)
    .WaitFor(assistant);

builder.Build().Run();
