# Real-World Examples

Short, focused patterns drawn directly from the IAW codebase.

## Reactive Fan-out

Multiple agents independently react to the same message. Orleans delivers to all `IReceiver<T>` implementations simultaneously — no coordinator needed.

```csharp
public class FormatterAgent : Agent, IReceiver<CodeChangedMessage>
{
    public async Task<MessageReceipt> ReceiveAsync(CodeChangedMessage msg, CancellationToken ct)
    {
        await GetResponse($"Run dotnet format on {msg.FilePath}", ct);
        return MessageReceipt.Accepted();
    }
}

public class TestAgent : Agent, IReceiver<CodeChangedMessage>
{
    public async Task<MessageReceipt> ReceiveAsync(CodeChangedMessage msg, CancellationToken ct)
    {
        await GetResponse($"Run tests for {msg.ProjectPath}", ct);
        return MessageReceipt.Accepted();
    }
}
```

## Self-Diagnostics

One line schedules a recurring health check. The AspireAgent uses MCP tools to query the running system and reports problems automatically.

```csharp
await project.ScheduleJob("System Health", TimeSpan.FromMinutes(5),
    "Check all Aspire resources. Report only unhealthy services.", ct);
```

## Context Enrichment

Every LLM call is automatically enriched with user preferences, project state, and relevant documents. Override `GetContextProviders()` to inject what matters for each agent.

```csharp
protected override IReadOnlyList<IAgentContextProvider> GetContextProviders() =>
[
    new UserContextProvider(GrainFactory),
    new ProjectContextProvider(durableState.Tasks, durableState.Files, durableState.ProjectMeta),
    new RAGContextProvider(qdrant, embeddings)
];
```

## Pub/Sub Streams

Typed events published to Orleans streams. Consumers auto-subscribe by declaring `IStreamConsumer<T>` — no wiring required.

```csharp
// Producer
await PublishToStream(new BuildCompletedEvent(ProjectPath, Success: true));

// Consumer — auto-subscribed via interface
public class DeployAgent : Agent, IStreamConsumer<BuildCompletedEvent>
{
    public Task OnStreamEventAsync(BuildCompletedEvent evt, StreamSequenceToken? token) =>
        GetResponse($"Deploy {evt.ProjectPath} — build {(evt.Success ? "green" : "red")}", default);
}
```
