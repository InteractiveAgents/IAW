using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<Qwen25>();

var samples = builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-silo", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 11_111;
    })
    .WithEndpoint("orleans-gateway", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 30_000;
    });

var ollama = builder.AddOllama("ollama").WithOpenWebUI().WithGPUSupport().WithDataVolume();
var qwen = ollama.AddModel("qwen2.5");

builder.AddProject<Projects.DevUI>("devui")
    .WithReference(qwen)
    .WithReference(iaw.AsClient())
    .WaitFor(qwen)
    .WaitFor(samples)
    .WithEnvironment("IAW__Orleans__Gateways__0", samples.GetEndpoint("orleans-gateway"));

var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true);
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);

var botToken = builder.AddParameter("bot-token", secret: true);
var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));

ngrok.WithTunnelEndpoint(telegramBot, "http");

builder.AddViteApp("website", "../../website")
    .WithNpm()
    .WithExternalHttpEndpoints();

builder.Build().Run();