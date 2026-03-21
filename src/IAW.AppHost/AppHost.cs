using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Gpt54Mini>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Gpt54Nano>()
    .WithLLM<Sonnet46>()
    .WithLLM<Opus46>()
    .WithLLM<Qwen25>()
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

var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true)
    .WithDescription("Get your authtoken at [dashboard.ngrok.com](https://dashboard.ngrok.com/get-started/your-authtoken)", enableMarkdown: true);
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);

var botToken = builder.AddParameter("bot-token", secret: true)
    .WithDescription("Create a bot and get the token from [@BotFather](https://t.me/BotFather) on Telegram", enableMarkdown: true);
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
