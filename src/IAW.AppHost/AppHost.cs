using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(e => e.WithDataVolume("iaw-blobs"));
var blobs = storage.AddBlobs("file-storage");

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume("iaw-qdrant");

// Production silo — hosts all agents, memory, LLM
var assistant = builder.AddProject<Projects.IAW_Assistant>("assistant")
    .WithReference(iaw)
    .WithReference(blobs)
    .WithReference(qdrant)
    .WaitFor(blobs)
    .WaitFor(qdrant)
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
    .WaitFor(assistant);

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithHttpEndpoint(port: 5300, name: "mcp-direct", isProxied: false)
    .WaitFor(assistant);

// Ngrok tunnel for Telegram webhook
var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true);
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);

// Telegram client
var botToken = builder.AddParameter("bot-token", secret: true);
var telegram = builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(iaw.AsClient())
    .WithReference(blobs)
    .WithReference(qdrant)
    .WithLLMEnvironment(builder)
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"))
    .WaitFor(assistant);

ngrok.WithTunnelEndpoint(telegram, "http");

// Documentation website (VitePress)
builder.AddViteApp("website", "../../website")
    .WithNpm()
    .WithExternalHttpEndpoints();

builder.Build().Run();