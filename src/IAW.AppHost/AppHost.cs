using Core.AI.Models;
using OpenAIModels = Core.AI.Models.OpenAI;
using AnthropicModels = Core.AI.Models.Anthropic;
using GitHubModels = Core.AI.Models.GitHub;
using OllamaModels = Core.AI.Models.Ollama;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")

    // --- OpenAI (direct API) ---
    //.WithLLM<OpenAIModels.Gpt54Nano>().AsFast()
    //.WithLLM<OpenAIModels.Gpt54Mini>().AsBalanced()
    //.WithLLM<OpenAIModels.Gpt54>().AsReasoning()
    //.WithEmbedding<OpenAIModels.TextEmbedding3Small>()

    // --- Anthropic (no embedding API — uses OpenAI embedding) ---
    //.WithLLM<AnthropicModels.Claude45Haiku>().AsFast()
    //.WithLLM<AnthropicModels.Sonnet46>().AsBalanced()
    //.WithLLM<AnthropicModels.Opus46>().AsReasoning()
    //.WithEmbedding<OpenAIModels.TextEmbedding3Small>()

    // --- GitHub Models (free/cheap, full tool calling) ---
    //.WithLLM<GitHubModels.Gpt41Nano>().AsFast()
    //.WithLLM<GitHubModels.Gpt41Mini>().AsBalanced()
    //.WithLLM<GitHubModels.O4Mini>().AsReasoning()
    //.WithEmbedding<GitHubModels.TextEmbedding3Small>()

    // --- Ollama (local, 3060 Ti / 8GB VRAM) ---
    .WithLLM<OllamaModels.Qwen25_7B>()
    .WithEmbedding<OllamaModels.MxbaiEmbedLarge>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI(op => op.WithLifetime(ContainerLifetime.Persistent)))

    // --- Local voice to text via Whisper with fallback to CPU in case of CUDA runtime issues ---
    .WithVoice2Text<WhisperLargeV3Turbo>();

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