using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<GitHubGpt4oMini>()
    .WithLLM<Qwen25>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());

var samples = builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder)
    .WaitFor(samples);

var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true);
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);

var botToken = builder.AddParameter("bot-token", secret: true);
var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));

ngrok.WithTunnelEndpoint(telegramBot, "http");

builder.AddViteApp("website", "../../website")
    .WithNpm()
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WaitFor(samples);

builder.Build().Run();
