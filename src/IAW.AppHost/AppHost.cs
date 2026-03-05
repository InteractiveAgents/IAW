using Aspire;
using Aspire.Hosting;
using Core.AI.Models;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var waitForExternalDependencies = builder.Configuration.GetValue("IAW:WaitForExternalDependencies", false);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<GitHubGpt4oMini>()
    .WithLLM<Qwen25>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());

var samples = builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30000; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11111; });

builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder)
    .WithEnvironment("Orleans__PrimaryGateway", samples.GetEndpoint("orleans-gateway"))
    .WaitFor(samples);

var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true);
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);

var qdrant = builder.AddQdrant("qdrant")
    .WithLifetime(ContainerLifetime.Persistent);

var botToken = builder.AddParameter("bot-token", secret: true);
var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithReference(qdrant)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30001; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11112; })
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));

if (waitForExternalDependencies)
    telegramBot.WaitFor(qdrant);

ngrok.WithTunnelEndpoint(telegramBot, "http");

builder.AddViteApp("website", "../../website")
    .WithNpm()
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Orleans__PrimaryGateway", samples.GetEndpoint("orleans-gateway"))
    .WaitFor(samples);

builder.Build().Run();
