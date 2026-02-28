using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>()
    .WithLLM<Claude45Haiku>();

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

var telegramToken = builder.AddParameter("telegram-bot-token", secret: true);
var telegramWebhookSecret = builder.AddParameter("telegram-webhook-secret", secret: true);
var telegramWebhookUrl = builder.AddParameter("telegram-webhook-url");
var telegramOwnerChatId = builder.AddParameter("telegram-owner-chat-id");

builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEnvironment("Telegram__BotToken", telegramToken)
    .WithEnvironment("Telegram__WebhookSecretToken", telegramWebhookSecret)
    .WithEnvironment("Telegram__WebhookUrl", telegramWebhookUrl)
    .WithEnvironment("Telegram__OwnerChatId", telegramOwnerChatId)
    .WithEndpoint("orleans-silo", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 11_112;
    })
    .WithEndpoint("orleans-gateway", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 30_001;
    })
    .WaitFor(samples);

builder.AddViteApp("website", "../../website")
    .WithNpm()
    .WithExternalHttpEndpoints();

builder.Build().Run();