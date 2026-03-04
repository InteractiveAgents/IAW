# Configuration

This page documents all configuration options for IAW projects: AppHost setup, LLM model configuration, Telegram bot settings, and Orleans parameters.

## AppHost Parameters

The Aspire AppHost uses secret parameters for API keys. These are declared in `appsettings.json` and injected as environment variables.

### Secret Parameters

| Parameter Name | Environment Variable | Description |
|---|---|---|
| `anthropic-api-key` | `AI__LLM__AnthropicApiKey` | Anthropic API key for Claude models |
| `openai-api-key` | `AI__LLM__OpenAiApiKey` | OpenAI API key |
| `github-token` | `GitHub__Token`, `AI__LLM__GitHubToken` | GitHub token for Octokit and GitHub-hosted models |
| `bot-token` | `Telegram__BotToken` | Telegram bot token from BotFather |
| `ngrok-auth-token` | (ngrok config) | Ngrok authentication token for tunnel setup |

### Passing Secrets at Runtime

Secrets can be set via Aspire user secrets or command-line arguments:

```bash
# Via user secrets (recommended for development)
dotnet user-secrets set "Parameters:anthropic-api-key" "sk-ant-..."

# Via command line
aspire run -- --Parameters:anthropic-api-key=sk-ant-...
```

## LLM Model Configuration

Models are registered in the AppHost via `WithLLM<TModel>()` and injected into silo projects via `WithLLMEnvironment()`.

### Declaring Models

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithLLM<Qwen25>();
```

### Environment Variable Format

`WithLLMEnvironment()` injects models as indexed environment variables:

```
AI__LLM__Models__0__Id=claude-4.5-haiku
AI__LLM__Models__0__Provider=Anthropic
AI__LLM__Models__0__ServiceKey=claude-4.5-haiku
AI__LLM__Models__1__Id=claude-sonnet-4.6
AI__LLM__Models__1__Provider=Anthropic
AI__LLM__Models__1__ServiceKey=claude-sonnet-4.6
```

### Available Model Types

Models are singletons in `Core.AI.Models`:

| Class | Provider | Model ID |
|---|---|---|
| `Claude45Haiku` | Anthropic | `claude-4.5-haiku` |
| `Sonnet46` | Anthropic | `claude-sonnet-4.6` |
| `GitHubGpt4oMini` | GitHub | `gpt-4o-mini` |
| `Qwen25` | Ollama | `qwen2.5` |

### Ollama Configuration

Ollama models automatically provision an Ollama container:

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());
```

Set `IAW:WaitForLlmModelResources=true` in configuration to wait for Ollama model downloads before starting silos.

### Injecting IChatClient in Grains

Use the `[Llm<TModel>]` attribute on constructor parameters:

```csharp
public class MyAgent(
    // ... durable state params ...
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : AgentV2(/* ... */)
{
}
```

The `LlmAttributeMapper<TModel>` resolves this to a keyed `IChatClient` registered by `AddLlmProviders()`.

## Telegram Configuration

### Environment Variables

| Variable | Description |
|---|---|
| `Telegram__BotToken` | Bot token from BotFather |
| `Telegram__NgrokApiUrl` | Ngrok API URL for auto-discovering the tunnel endpoint |

### AppHost Setup

```csharp
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

ngrok.WithTunnelEndpoint(telegramBot, "http");
```

## Orleans Settings

### Silo Configuration

Each silo needs distinct ports when running multiple silos in the same cluster:

| Endpoint | Default (samples) | Telegram Bot |
|---|---|---|
| `orleans-silo` | 11111 | 11112 |
| `orleans-gateway` | 30000 | 30001 |

### AddIAW Defaults

`AddIAW()` configures Orleans with:

| Setting | Value |
|---|---|
| Cluster ID | `"dev"` |
| Service ID | `"dev"` |
| Clustering | Development (localhost) |
| Default Storage | In-memory |
| PubSubStore | In-memory |
| Stream Provider | Memory streams (`"agents"`) |
| Reminders | In-memory |

### Client Connection

Non-silo projects connect as Orleans clients:

```csharp
builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Orleans__PrimaryGateway", samples.GetEndpoint("orleans-gateway"));
```

The client reads `Orleans:PrimaryGateway` to determine the gateway port for static clustering.

## IAW-Specific Configuration

| Key | Default | Description |
|---|---|---|
| `IAW:WaitForExternalDependencies` | `false` | Wait for container resources (Qdrant, etc.) before starting silos |
| `IAW:WaitForLlmModelResources` | `false` | Wait for Ollama model downloads before starting silos |
