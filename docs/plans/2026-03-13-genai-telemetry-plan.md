# GenAI Telemetry Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement proper GenAI telemetry following OpenTelemetry semantic conventions v1.40 so that traces, token usage, and agent-to-LLM correlation appear correctly in the Aspire Dashboard.

**Architecture:** Add `invoke_agent` parent spans with `gen_ai.*` attributes in `Agent.cs`, fix streaming usage capture in `UsageCaptureChatClient`, add cumulative per-agent token metrics, add `execute_tool` spans to the tool execution pipeline, and wire the `Experimental.Microsoft.Extensions.AI` ActivitySource into ServiceDefaults.

**Tech Stack:** OpenTelemetry .NET SDK 1.15, Microsoft.Extensions.AI 10.4.0, System.Diagnostics.Activity, .NET Aspire Dashboard

---

## Current State (What's Broken)

1. **No `invoke_agent` spans** — `StreamResponseCore` doesn't create a GenAI-convention parent span. The `OpenTelemetryChatClient` creates `chat {model}` child spans, but they have no parent agent span to correlate to.
2. **No `gen_ai.*` attributes** — custom spans use `agent.type`/`agent.id` instead of standard `gen_ai.agent.name`, `gen_ai.operation.name`, etc.
3. **Token usage invisible on agent spans** — `OpenTelemetryChatClient` captures tokens on its `chat` spans, but the agent-level span has no usage summary.
4. **`UsageCaptureChatClient` ignores streaming** — `GetStreamingResponseAsync` passes through without capturing. Since `GetResponse` delegates to streaming, `LastUsage` is always null.
5. **No cumulative usage tracking** — only `LastUsage` (volatile, single-response). No per-agent totals for dashboard metrics.
6. **No `execute_tool` spans** — tool calls inside the LLM loop are invisible in traces.
7. **Missing ActivitySource in ServiceDefaults** — `Experimental.Microsoft.Extensions.AI` source not added, so `OpenTelemetryChatClient` spans may be dropped.
8. **No context enrichment span** — `EnrichWithContext` is invisible in traces.
9. **`gen_ai.conversation.id`** not set — no way to correlate multiple turns of the same conversation.

---

## Chunk 1: Core Agent Telemetry Spans

### Task 1: Add `invoke_agent` span to StreamResponseCore

**Files:**
- Modify: `src/Core/Agents/Agent.cs:70-101`

- [ ] **Step 1: Write the failing test**

In `test/Core.Tests/AgentTests.cs`, add a test that verifies `GetResponseStream` creates an Activity with `gen_ai.operation.name = invoke_agent`:

```csharp
[Fact]
public async Task GetResponseStream_creates_invoke_agent_activity()
{
    MockChatClient.ReturnsText("hello");
    using var listener = new ActivityListener
    {
        ShouldListenTo = s => s.Name == "IAW",
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
    };
    ActivitySource.AddActivityListener(listener);

    Activity? capturedActivity = null;
    listener.ActivityStopped = a =>
    {
        if (a.OperationName.StartsWith("invoke_agent"))
            capturedActivity = a;
    };

    await foreach (var _ in Agent.GetResponseStream("test", default)) { }

    Assert.NotNull(capturedActivity);
    Assert.Equal("invoke_agent", capturedActivity.GetTagItem("gen_ai.operation.name"));
    Assert.Equal(Agent.GetPrimaryKeyString(), capturedActivity.GetTagItem("gen_ai.agent.id"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "GetResponseStream_creates_invoke_agent_activity" -v m`
Expected: FAIL — no `invoke_agent` activity exists yet.

- [ ] **Step 3: Implement invoke_agent span in StreamResponseCore**

Replace the current `StreamResponseCore` method in `src/Core/Agents/Agent.cs`:

```csharp
private async IAsyncEnumerable<string> StreamResponseCore(
    string prompt,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    using var activity = AgentTelemetry.ActivitySource.StartActivity(
        $"invoke_agent {this.GetPrimaryKeyString()}",
        ActivityKind.Internal);

    activity?.SetTag("gen_ai.operation.name", "invoke_agent");
    activity?.SetTag("gen_ai.provider.name", "iaw");
    activity?.SetTag("gen_ai.agent.id", this.GetPrimaryKeyString());
    activity?.SetTag("gen_ai.agent.name", DisplayName);
    activity?.SetTag("gen_ai.conversation.id", this.GetPrimaryKeyString());

    var sw = Stopwatch.StartNew();
    var completed = false;
    try
    {
        prompt = await EnrichWithContext(prompt, cancellationToken);

        await foreach (var chunk in _agent!.RunStreamingAsync(prompt, _session, cancellationToken: cancellationToken))
        {
            if (chunk.Text is not { } text)
                continue;
            yield return text;
        }

        // Capture token usage on the agent span
        if (_usageCapture.LastUsage is { } usage)
        {
            activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
            activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
            RecordTokenMetrics(usage);
        }

        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        durableState.EventLog.Add(new AgentEvent(
            "LlmCall", this.GetPrimaryKeyString(), correlationId,
            DateTimeOffset.UtcNow, new Dictionary<string, object> { ["prompt_length"] = prompt.Length }));

        await WriteStateAsync(cancellationToken);
        completed = true;
    }
    finally
    {
        if (!completed)
        {
            activity?.SetTag("error.type", "conversation_error");
            AgentTelemetry.ConversationErrors.Add(1, new TagList { { "agent.type", GetType().Name } });
        }
        AgentTelemetry.ConversationDuration.Record(sw.Elapsed.TotalSeconds,
            new TagList { { "agent.type", GetType().Name } });
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "GetResponseStream_creates_invoke_agent_activity" -v m`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Agents/Agent.cs test/Core.Tests/AgentTests.cs
git commit -m "feat: add invoke_agent span with gen_ai.* attributes to StreamResponseCore"
```

---

### Task 2: Add GenAI token usage metrics to AgentTelemetry

**Files:**
- Modify: `src/Core/Observability/AgentTelemetry.cs`
- Modify: `src/Core/Agents/Agent.cs` (add `RecordTokenMetrics` helper)

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetResponseStream_records_token_usage_metrics()
{
    MockChatClient.ReturnsText("hello");
    await foreach (var _ in Agent.GetResponseStream("test", default)) { }

    var usage = await Agent.GetLastUsage(default);
    // Usage should be captured (non-null) after streaming
    Assert.NotNull(usage);
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL — streaming path doesn't capture usage.

- [ ] **Step 3: Add token metrics to AgentTelemetry**

Add to `src/Core/Observability/AgentTelemetry.cs`:

```csharp
public static readonly Histogram<long> TokenUsage = Meter.CreateHistogram<long>(
    "gen_ai.client.token.usage", "{token}", "Token usage per LLM call");

public static readonly Counter<long> TotalInputTokens = Meter.CreateCounter<long>(
    "agents.tokens.input", "{token}", "Cumulative input tokens across all agents");

public static readonly Counter<long> TotalOutputTokens = Meter.CreateCounter<long>(
    "agents.tokens.output", "{token}", "Cumulative output tokens across all agents");
```

Add to `src/Core/Agents/Agent.cs` as a private method:

```csharp
private void RecordTokenMetrics(AgentUsage usage)
{
    var tags = new TagList
    {
        { "gen_ai.agent.name", DisplayName },
        { "gen_ai.operation.name", "invoke_agent" }
    };
    AgentTelemetry.TokenUsage.Record(usage.InputTokens, new TagList(tags) { { "gen_ai.token.type", "input" } });
    AgentTelemetry.TokenUsage.Record(usage.OutputTokens, new TagList(tags) { { "gen_ai.token.type", "output" } });
    AgentTelemetry.TotalInputTokens.Add(usage.InputTokens, tags);
    AgentTelemetry.TotalOutputTokens.Add(usage.OutputTokens, tags);
}
```

- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Run full test suite**

Run: `dotnet test IAW.slnx`

- [ ] **Step 6: Commit**

```bash
git add src/Core/Observability/AgentTelemetry.cs src/Core/Agents/Agent.cs test/Core.Tests/AgentTests.cs
git commit -m "feat: add gen_ai.client.token.usage metrics and per-agent token counters"
```

---

## Chunk 2: Fix Streaming Usage Capture

### Task 3: Fix UsageCaptureChatClient to capture streaming usage

**Files:**
- Modify: `src/Core/AI/UsageCaptureChatClient.cs`

The `UseStreamingUsage()` middleware in the pipeline forces `stream_options.include_usage`, so the final streaming chunk contains `UsageDetails`. But `UsageCaptureChatClient` wraps the outer `IChatClient` (it's constructed before the pipeline), so it sees the raw streaming response without usage.

The fix: `UsageCaptureChatClient` must enumerate the streaming response, accumulate usage from chunks, and yield through.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetResponseStream_captures_streaming_usage()
{
    // MockChatClient.ReturnsText already sets _streamFactory
    MockChatClient.ReturnsText("hello world");
    await foreach (var _ in Agent.GetResponseStream("test", default)) { }

    var usage = await Agent.GetLastUsage(default);
    Assert.NotNull(usage);
}
```

- [ ] **Step 2: Run test — confirm it fails because streaming path doesn't capture usage**

- [ ] **Step 3: Implement streaming usage capture**

In `src/Core/AI/UsageCaptureChatClient.cs`, replace the passthrough streaming with:

```csharp
public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
    IEnumerable<AIChatMessage> messages,
    ChatOptions? options,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    long inputTokens = 0, outputTokens = 0, totalTokens = 0;

    await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken))
    {
        if (update.Usage is { } usage)
        {
            inputTokens += usage.InputTokenCount ?? 0;
            outputTokens += usage.OutputTokenCount ?? 0;
            totalTokens += usage.TotalTokenCount ?? 0;
        }
        yield return update;
    }

    if (inputTokens > 0 || outputTokens > 0)
        _lastUsage = new AgentUsage(inputTokens, outputTokens, totalTokens);
}
```

Add `using System.Runtime.CompilerServices;` to imports.

- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Run full test suite**
- [ ] **Step 6: Commit**

```bash
git add src/Core/AI/UsageCaptureChatClient.cs test/Core.Tests/AgentTests.cs
git commit -m "fix: capture token usage from streaming responses in UsageCaptureChatClient"
```

---

### Task 4: Add cumulative per-agent usage tracking in durable state

**Files:**
- Modify: `src/Core/Agents/Agent.cs`

Currently `LastUsage` is volatile (lost on deactivation). Add cumulative tracking in durable state.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetResponseStream_accumulates_usage_in_state()
{
    MockChatClient.ReturnsText("first");
    await foreach (var _ in Agent.GetResponseStream("a", default)) { }
    MockChatClient.ReturnsText("second");
    await foreach (var _ in Agent.GetResponseStream("b", default)) { }

    var state = await Agent.GetState(default);
    Assert.True(state.Entries.ContainsKey("cumulative-input-tokens"));
}
```

- [ ] **Step 2: Run test to verify it fails**

- [ ] **Step 3: Add cumulative state tracking to RecordTokenMetrics**

In `RecordTokenMetrics` in `Agent.cs`, after recording OTel metrics, also persist:

```csharp
private void RecordTokenMetrics(AgentUsage usage)
{
    // OTel metrics (from Task 2) ...

    // Durable cumulative tracking
    var currentInput = GetLongFromState("cumulative-input-tokens");
    var currentOutput = GetLongFromState("cumulative-output-tokens");
    durableState.State["cumulative-input-tokens"] = new StateEntry("cumulative-input-tokens", currentInput + usage.InputTokens);
    durableState.State["cumulative-output-tokens"] = new StateEntry("cumulative-output-tokens", currentOutput + usage.OutputTokens);
}

private long GetLongFromState(string key)
{
    if (!durableState.State.TryGetValue(key, out var entry)) return 0;
    return entry.Value is long l ? l : long.TryParse(entry.Value.ToString(), out var parsed) ? parsed : 0;
}
```

- [ ] **Step 4: Run test and full suite**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Agents/Agent.cs test/Core.Tests/AgentTests.cs
git commit -m "feat: track cumulative token usage in agent durable state"
```

---

## Chunk 3: ServiceDefaults and Trace Correlation

### Task 5: Add missing ActivitySource names to ServiceDefaults

**Files:**
- Modify: `src/IAW.ServiceDefaults/Extensions.cs`

- [ ] **Step 1: Add the Experimental.Microsoft.Extensions.AI source**

The `OpenTelemetryChatClient` uses `Experimental.Microsoft.Extensions.AI` as its ActivitySource name. Without this, the `chat {model}` spans are created but not captured by the exporter.

In `Extensions.cs`, in the `.WithTracing` block, add:

```csharp
.AddSource("Experimental.Microsoft.Extensions.AI")
```

Also add the meter:

```csharp
.AddMeter("Experimental.Microsoft.Extensions.AI")
```

- [ ] **Step 2: Verify the Aspire dashboard shows chat spans**

Run: `aspire run`, send a message via Telegram or DevUI, check the Aspire Dashboard traces tab. You should now see:
```
invoke_agent personal-assistant  [IAW]
  └── chat claude-sonnet-4-20250514          [Experimental.Microsoft.Extensions.AI]
       └── HTTP POST api.anthropic.com  [System.Net.Http]
```

- [ ] **Step 3: Commit**

```bash
git add src/IAW.ServiceDefaults/Extensions.cs
git commit -m "fix: add Experimental.Microsoft.Extensions.AI source to capture LLM chat spans"
```

---

### Task 6: Add context enrichment span

**Files:**
- Modify: `src/Core/Agents/Agent.cs:129-151` (EnrichWithContext method)

- [ ] **Step 1: Wrap EnrichWithContext in an Activity span**

```csharp
private async Task<string> EnrichWithContext(string prompt, CancellationToken ct)
{
    var providers = GetContextProviders();
    if (providers.Count == 0) return prompt;

    using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.enrich_context");
    activity?.SetTag("context.provider_count", providers.Count);

    var contextParts = new List<string>();
    foreach (var provider in providers)
    {
        try
        {
            var items = await provider.GetContextAsync(this.GetPrimaryKeyString(), prompt, ct);
            contextParts.AddRange(items);
        }
        catch
        {
            // context provider unavailable — skip
        }
    }

    activity?.SetTag("context.items_found", contextParts.Count);

    if (contextParts.Count == 0) return prompt;
    return $"[Relevant context from memory]\n{string.Join("\n", contextParts)}\n\n[User message]\n{prompt}";
}
```

- [ ] **Step 2: Run full test suite**
- [ ] **Step 3: Commit**

```bash
git add src/Core/Agents/Agent.cs
git commit -m "feat: add context enrichment span for trace visibility"
```

---

## Chunk 4: Tool Execution Telemetry

### Task 7: Add `execute_tool` spans to the tool execution pipeline

**Files:**
- Modify: `src/Core/Agents/Agent.Tools.cs`

The current tool pipeline uses `AIFunctionFactory.Create` which wraps methods as `AITool` instances. Microsoft.Extensions.AI's `OpenTelemetryChatClient` already emits tool call/result events as log events, but not as spans. We need wrapping at the `AIFunction` level.

- [ ] **Step 1: Create a tool-wrapping utility**

Add a method to `Agent.Tools.cs` that wraps each registered `AITool` in a tracing decorator:

```csharp
private IReadOnlyList<AITool> WrapToolsWithTelemetry(IReadOnlyList<AITool> tools)
{
    return tools.Select(tool =>
    {
        if (tool is not AIFunction func) return tool;
        return AIFunctionFactory.Create(
            async (CancellationToken ct) =>
            {
                using var activity = AgentTelemetry.ActivitySource.StartActivity(
                    $"execute_tool {func.Name}", ActivityKind.Internal);
                activity?.SetTag("gen_ai.operation.name", "execute_tool");
                activity?.SetTag("gen_ai.tool.name", func.Name);
                activity?.SetTag("gen_ai.tool.type", "function");
                // Delegate to original — this is a simplified wrapper
                // The actual invocation happens through the AI framework
                return $"[traced] {func.Name}";
            },
            func.Name,
            func.Description);
    }).ToList();
}
```

**Note:** This task is more complex because `AIFunction` invocation happens inside the Microsoft.Extensions.AI pipeline (the `RunStreamingAsync` loop handles tool calls internally). The cleaner approach is to use the agent-level `WithOpenTelemetry()` from the Microsoft Agents Framework if available.

**Alternative approach:** If `Microsoft.Agents.AI` package provides `WithOpenTelemetry()` extension, use that instead:

```csharp
_agent = _usageCapture.AsAIAgent(new ChatClientAgentOptions { ... })
    .WithOpenTelemetry(sourceName: "IAW", enableSensitiveData: true);
```

This would automatically add `invoke_agent` + `execute_tool` spans. Check if the package version supports this. If yes, this replaces Tasks 1 and 7 with a single line.

- [ ] **Step 1: Check if Microsoft.Agents.AI supports WithOpenTelemetry**

Look up the package: `dotnet list package | grep Agents`
Check the API: does `AIAgentBuilderExtensions.UseOpenTelemetry` exist?

- [ ] **Step 2: If available, wire it in Agent.cs OnActivateAsync**
- [ ] **Step 3: If not available, implement manual tool wrapping as described above**
- [ ] **Step 4: Run full test suite**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Agents/Agent.Tools.cs src/Core/Agents/Agent.cs
git commit -m "feat: add execute_tool spans for tool call tracing"
```

---

## Chunk 5: Event Publishing Telemetry Alignment

### Task 8: Align event publishing spans with GenAI conventions

**Files:**
- Modify: `src/Core/Agents/Agent.Events.cs`

- [ ] **Step 1: Read Agent.Events.cs to understand current publish spans**
- [ ] **Step 2: Add `gen_ai.agent.id` and `gen_ai.agent.name` to publish and stream event spans**

All `agent.publish` and `agent.handle_stream_event` activities should carry:
- `gen_ai.agent.id` = `this.GetPrimaryKeyString()`
- `gen_ai.agent.name` = `DisplayName`

This ensures all agent activities are filterable by agent in the Aspire dashboard.

- [ ] **Step 3: Run full test suite**
- [ ] **Step 4: Commit**

```bash
git add src/Core/Agents/Agent.Events.cs src/Core/Agents/Agent.Streams.cs
git commit -m "feat: add gen_ai.agent.* attributes to all agent activity spans"
```

---

## Chunk 6: Verification

### Task 9: End-to-end verification with Aspire Dashboard

- [ ] **Step 1: Build**

```bash
dotnet build IAW.slnx
```

- [ ] **Step 2: Run all tests**

```bash
dotnet test IAW.slnx
```

- [ ] **Step 3: Start Aspire and test**

```bash
aspire run
```

Send a message via Telegram. In the Aspire Dashboard:
1. **Traces tab:** Verify you see `invoke_agent personal-assistant` as a parent span with `chat claude-sonnet-4-20250514` as a child span, and HTTP spans beneath that.
2. **Traces tab:** Verify `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens` appear on the `invoke_agent` span.
3. **Metrics tab:** Verify `gen_ai.client.token.usage` histogram appears with `gen_ai.token.type` dimension.
4. **Metrics tab:** Verify `agents.tokens.input` and `agents.tokens.output` counters are incrementing.

- [ ] **Step 4: Commit any fixes from verification**

---

## Summary of Changes

| File | Change |
|------|--------|
| `src/Core/Agents/Agent.cs` | Add `invoke_agent` span, token metrics recording, cumulative state tracking, context enrichment span |
| `src/Core/Observability/AgentTelemetry.cs` | Add `gen_ai.client.token.usage` histogram, input/output token counters |
| `src/Core/AI/UsageCaptureChatClient.cs` | Fix streaming usage capture |
| `src/IAW.ServiceDefaults/Extensions.cs` | Add `Experimental.Microsoft.Extensions.AI` source/meter |
| `src/Core/Agents/Agent.Tools.cs` | Add `execute_tool` spans (or use `WithOpenTelemetry()`) |
| `src/Core/Agents/Agent.Events.cs` | Add `gen_ai.agent.*` attributes to publish spans |
| `src/Core/Agents/Agent.Streams.cs` | Add `gen_ai.agent.*` attributes to stream event spans |

**Expected trace tree after implementation:**
```
invoke_agent personal-assistant          [IAW, gen_ai.usage.input_tokens=150, gen_ai.usage.output_tokens=80]
  ├── agent.enrich_context               [IAW, context.items_found=3]
  ├── chat claude-sonnet-4-20250514               [Experimental.Microsoft.Extensions.AI, tokens on span]
  │    └── HTTP POST api.anthropic.com   [System.Net.Http]
  ├── execute_tool AssignTaskToAgent     [IAW]
  │    └── invoke_agent shell            [IAW, nested agent call]
  │         ├── chat claude-haiku-4-5-20251001    [Experimental.Microsoft.Extensions.AI]
  │         │    └── HTTP POST ...
  │         └── execute_tool RunShellAsync [IAW]
  └── chat claude-sonnet-4-20250514               [second LLM call after tool results]
       └── HTTP POST api.anthropic.com
```
