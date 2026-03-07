# Conversation Behavior

Every V3 agent has built-in conversation capabilities powered by `Microsoft.Extensions.AI` and the Microsoft Agent Framework. This page covers `GetResponse`, `GetResponseStream`, conversation history, and context providers.

## GetResponse

The primary way to interact with an agent. Sends a prompt and returns the full response:

```csharp
var agent = grainFactory.GetGrain<IAgent>("assistant");
var response = await agent.GetResponse("What is Orleans?", ct);
Console.WriteLine(response);
```

Under the hood, `GetResponse`:
1. Passes the prompt to the `AIAgent` (from Microsoft.Agents.AI)
2. The `AIAgent` builds the chat messages from history + system prompt
3. Calls the `IChatClient` (LLM provider)
4. Executes any tool calls automatically
5. Persists the conversation to durable history
6. Returns the final text

## GetResponseStream

For streaming responses token-by-token. Returns an `IAsyncEnumerable<string>`:

```csharp
await foreach (var chunk in agent.GetResponseStream("Tell me about agents", ct))
{
    Console.Write(chunk);
}
```

Streaming is useful for real-time UIs where you want to show text as it arrives from the LLM. The conversation is persisted to durable history after the stream completes.

## Conversation History

History is stored in a `IDurableList<ChatMessage>` -- a journaled Orleans collection that survives grain deactivation and silo restarts.

### Reading History

```csharp
var history = await agent.GetHistory(ct);
foreach (var msg in history)
{
    Console.WriteLine($"[{msg.TimestampUtc:u}] {msg.Role}: {msg.Content}");
}
```

The `ChatMessage` record:

```csharp
[GenerateSerializer]
public sealed record ChatMessage
{
    [Id(0)] public string Role { get; init; } = string.Empty;
    [Id(1)] public string Content { get; init; } = string.Empty;
    [Id(2)] public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

### Clearing History

Reset the conversation:

```csharp
await agent.ClearHistoryAsync(ct);
```

This clears the durable list and creates a new `AIAgent` session.

## DurableChatHistoryProvider

The `DurableChatHistoryProvider` bridges Orleans durable state with the Microsoft Agent Framework's `ChatHistoryProvider`:

- **ProvideChatHistoryAsync**: Reads from the durable `history` list and converts to `Microsoft.Extensions.AI.ChatMessage` objects
- **StoreChatHistoryAsync**: Writes both request and response messages back to the durable list

This means conversation history persists across grain deactivations, silo restarts, and rebalancing.

## Instructions (System Prompt)

The `Instructions` property provides the system prompt for every LLM call:

```csharp
protected override string Instructions =>
    "You are a code review expert. Focus on security, performance, and readability.";
```

The system prompt is set once during `OnActivateAsync` when the `AIAgent` is created. To change instructions, the grain must be deactivated and reactivated.

## Context Providers

For advanced scenarios, implement `IAIContextProvider` to inject additional context into conversations:

```csharp
using Core.V3.Context;

public interface IAIContextProvider
{
    Task<AIContext> ProvideContextAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct);
    Task StoreContextAsync(
        IReadOnlyList<ChatMessage> request, AgentResponse response, CancellationToken ct);
}
```

Context providers can inject relevant documents, project state, or other data before the LLM processes a prompt.

## HTTP Endpoint Example

Expose an agent's conversation via HTTP:

```csharp
app.MapPost("/chat/{agentId}", async (
    IGrainFactory grains, string agentId, ChatRequest request) =>
{
    var agent = grains.GetGrain<IAgent>(agentId);
    var response = await agent.GetResponse(request.Prompt, default);
    return new { response };
});

app.MapGet("/chat/{agentId}/history", async (
    IGrainFactory grains, string agentId) =>
{
    var agent = grains.GetGrain<IAgent>(agentId);
    return await agent.GetHistory(default);
});

app.MapPost("/chat/{agentId}/clear", async (
    IGrainFactory grains, string agentId) =>
{
    var agent = grains.GetGrain<IAgent>(agentId);
    await agent.ClearHistoryAsync(default);
    return Results.Ok();
});

record ChatRequest(string Prompt);
```

## Streaming HTTP Example

Use server-sent events for streaming:

```csharp
app.MapPost("/chat/{agentId}/stream", async (
    IGrainFactory grains, string agentId, ChatRequest request, HttpContext http) =>
{
    http.Response.ContentType = "text/event-stream";
    var agent = grains.GetGrain<IAgent>(agentId);

    await foreach (var chunk in agent.GetResponseStream(request.Prompt, http.RequestAborted))
    {
        await http.Response.WriteAsync($"data: {chunk}\n\n");
        await http.Response.Body.FlushAsync();
    }
});
```
