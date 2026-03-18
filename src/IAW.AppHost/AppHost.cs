using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text<WhisperLargeV3Turbo>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI())
    .WithWorkspace("D:\\IAW-Workspace");

var assistant = builder.AddProject<Projects.IAW_Assistant>("assistant")
    .WithReference(iaw)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30000; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11111; })
    .WithUrlForEndpoint("https", ep => new()
    {
        Url = "/dashboard",
        DisplayText = "Orleans Dashboard"
    });

builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WaitFor(assistant);

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithHttpEndpoint(port: 5300, name: "mcp-direct", isProxied: false)
    .WaitFor(assistant);

var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true);
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);

var botToken = builder.AddParameter("bot-token", secret: true);
var telegram = builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"))
    .WaitFor(assistant);

ngrok.WithTunnelEndpoint(telegram, "http");

builder.AddViteApp("website", "../../website")
    .WithNpm()
    .WithExternalHttpEndpoints();

builder.Build().Run();
