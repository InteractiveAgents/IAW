# Token Usage Loop Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate token waste and tool-calling loops in the Telegram → Thread → TelegramUIAgent flow.

**Architecture:** Four root causes were identified from trace `7dc9ac11c0f1597dc7f7826d75f46457`: (1) TelegramUIAgent uses the full Agent pipeline with tool-calling loop (FormatResponse is exposed as an LLM tool via DiscoverInterfaceTools, creating recursive calls), (2) TelegramUIAgent maintains durable history unnecessarily, (3) ThreadAgent sends 100 recent messages per call, (4) TelegramUIAgent is called for every response > 200 chars even when no rich formatting is needed. Fixes are minimal, targeted changes to existing files — no new files.

**Tech Stack:** C# / .NET 11 / Orleans / Microsoft.Extensions.AI

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Agents/Orchestration/TelegramUIAgent.cs` | Modify | Bypass Agent pipeline — use ChatClient directly, no tools, no history |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Lower MaxHistoryMessages from 100 → 20 |
| `src/Clients.Telegram/TelegramBotService.cs` | Modify | Add heuristic to skip TelegramUIAgent when response has no rich structure |
| `src/Core/Agents/Agent.Tools.cs` | Modify | Exclude methods returning non-primitive/non-string types from interface tool discovery |
| `test/Core.Tests/TelegramUIAgentTests.cs` | Modify | Update tests for new direct-ChatClient behavior |
| `test/Core.Tests/ThreadTests.cs` | Modify | Add test verifying MaxHistoryMessages = 20 |

---

### Task 1: Fix TelegramUIAgent — bypass Agent pipeline, use ChatClient directly

The core loop bug: `FormatResponse` → `GetResponse` → `RunStreamingAsync` → LLM sees `FormatResponse` as a tool (via `DiscoverInterfaceTools`) → calls it → recursive loop. Also, each call uses full durable history pipeline unnecessarily.

**Files:**
- Modify: `src/Agents/Orchestration/TelegramUIAgent.cs`
- Modify: `test/Core.Tests/TelegramUIAgentTests.cs`

- [ ] **Step 1: Update TelegramUIAgent to use ChatClient directly**

Replace `GetResponse()` call with a direct `ChatClient.GetResponseAsync()` — no tools, no history, no Agent pipeline:

```csharp
public async Task<RichOutput> FormatResponse(string rawText, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(rawText))
        return new RichOutput("", []);

    try
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, $"Format this response for Telegram. Return ONLY valid JSON.\n\nRESPONSE TEXT:\n{rawText}")
        };

        var response = await ChatClient.GetResponseAsync(messages, new ChatOptions
        {
            MaxOutputTokens = 2048
        }, ct);

        return ParseRichOutput(response.Text ?? "", rawText);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "TelegramUI formatting failed, returning plain text");
        return new RichOutput(rawText, []);
    }
}
```

Key changes:
- Uses `ChatClient` property directly (bypasses Agent pipeline, tools, and history)
- Passes `Instructions` as system message inline (no tool registration)
- `MaxOutputTokens = 2048` (formatting never needs 4096)
- No `WriteStateAsync` call — nothing to persist

- [ ] **Step 2: Override MaxHistoryMessages to 0 and remove tool registration**

Add overrides to prevent any tools or history from being registered on activation:

```csharp
protected override int MaxHistoryMessages => 0;
protected override IReadOnlyList<AITool> DefineTools() => [];
protected override IReadOnlyList<AITool> DefineAdditionalTools() => [];
```

This ensures even if `OnActivateAsync` runs the base Agent setup, it won't register tools or load history.

- [ ] **Step 3: Run existing TelegramUIAgent tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~TelegramUIAgent" -v normal`
Expected: PASS (tests verify FormatResponse output parsing, not the internal pipeline)

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/TelegramUIAgent.cs test/Core.Tests/TelegramUIAgentTests.cs
git commit -m "fix: bypass Agent pipeline in TelegramUIAgent to prevent tool-calling loop and token waste"
```

---

### Task 2: Lower ThreadAgent history window from 100 → 20

ThreadAgent sends up to 100 recent messages to the LLM per call. With tool call round-trips (each adds user+assistant messages), this grows fast. The ChatReducer + HistorySummarizer handle older messages, so 20 recent is sufficient.

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`
- Modify: `test/Core.Tests/ThreadTests.cs`

- [ ] **Step 1: Write failing test for MaxHistoryMessages**

In `test/Core.Tests/ThreadTests.cs`, add:

```csharp
[Fact]
public void ThreadAgent_MaxHistoryMessages_Is20()
{
    // ThreadAgent should use a conservative history window to limit token usage
    // The ChatReducer + HistorySummarizer handle older messages via summarization
    var prop = typeof(ThreadAgent)
        .GetProperty("MaxHistoryMessages", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(prop);

    var agent = Agent(UniqueId("thread-hist"));
    var value = prop!.GetValue(agent);
    Assert.Equal(20, value);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ThreadAgent_MaxHistoryMessages" -v normal`
Expected: FAIL (current value is 100 from base Agent)

- [ ] **Step 3: Add MaxHistoryMessages override to ThreadAgent**

In `src/Agents/Orchestration/ThreadAgent.cs`, add inside the class:

```csharp
protected override int MaxHistoryMessages => 20;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ThreadAgent_MaxHistoryMessages" -v normal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs test/Core.Tests/ThreadTests.cs
git commit -m "fix: lower ThreadAgent history window to 20 to reduce token usage per call"
```

---

### Task 3: Add heuristic to skip TelegramUIAgent for plain-text responses

Currently every response > 200 chars triggers a full TelegramUIAgent LLM call. Most responses (greetings, explanations, delegation confirmations) don't need rich formatting. Add a cheap heuristic to detect when rich formatting is likely useful.

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

- [ ] **Step 1: Add NeedsRichFormatting heuristic method**

Add a static method in `TelegramBotService`:

```csharp
static bool NeedsRichFormatting(string text)
{
    // numbered list pattern: "1." or "1)" at line start
    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?m)^\s*\d+[\.\)]\s"))
        return true;

    // bullet list with 3+ items
    var bulletCount = System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^\s*[-*•]\s").Count;
    if (bulletCount >= 3)
        return true;

    // markdown headers (## or ###)
    if (text.Contains("\n##"))
        return true;

    // explicit option/choice language
    if (text.Contains("Option ", StringComparison.OrdinalIgnoreCase) &&
        text.Contains("Option 2", StringComparison.OrdinalIgnoreCase))
        return true;

    return false;
}
```

- [ ] **Step 2: Guard TelegramUIAgent call in StreamResponseAsync with heuristic**

Replace the TelegramUIAgent block (lines 731-754) with:

```csharp
if (NeedsRichFormatting(finalText))
{
    try
    {
        var uiAgent = clusterClient.GetGrain<ITelegramUI>($"tg-ui-{Guid.NewGuid().ToString("N")[..8]}");
        var richOutput = await uiAgent.FormatResponse(finalText, ct);

        if (richOutput.Parts.Count > 0)
        {
            await RenderRichOutput(chatId, currentMessageId, topicId, richOutput, telegramId, ct);
        }
        else if (!string.IsNullOrEmpty(richOutput.FormattedText))
        {
            await EditWithMarkdown(chatId, currentMessageId, richOutput.FormattedText);
        }
        else
        {
            await EditSafe(chatId, currentMessageId, finalText);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "TelegramUI formatting failed for user {TelegramId}, falling back to plain text", telegramId);
        await EditSafe(chatId, currentMessageId, finalText);
    }
}
else
{
    await EditSafe(chatId, currentMessageId, finalText);
}
```

- [ ] **Step 3: Apply same guard in SendJobResultAsync**

In `SendJobResultAsync` (around line 432), wrap the TelegramUIAgent call with the same heuristic:

```csharp
if (NeedsRichFormatting(formattedText))
{
    try
    {
        var uiAgent = clusterClient.GetGrain<ITelegramUI>($"tg-ui-{Guid.NewGuid().ToString("N")[..8]}");
        var richOutput = await uiAgent.FormatResponse(formattedText, ct);
        // ... existing rendering logic
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "TelegramUI formatting failed for job result, keeping plain text");
    }
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds with no warnings

- [ ] **Step 5: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "fix: skip TelegramUIAgent LLM call for plain-text responses without rich structure"
```

---

### Task 4: Prevent interface methods returning complex types from being registered as tools

`DiscoverInterfaceTools` in `Agent.Tools.cs` registers ALL declared interface methods as LLM tools. This means `ITelegramUI.FormatResponse` (returns `Task<RichOutput>`) gets exposed as a tool to the LLM, enabling recursive calls. Methods returning domain types (not string/primitives) should be excluded since they aren't useful as LLM tools.

**Files:**
- Modify: `src/Core/Agents/Agent.Tools.cs`
- Modify: `test/Core.Tests/AgentToolDiscoveryTests.cs`

- [ ] **Step 1: Write failing test**

In `test/Core.Tests/AgentToolDiscoveryTests.cs`, add a test verifying FormatResponse-like methods are excluded:

```csharp
[Fact]
public void DiscoverInterfaceTools_ExcludesMethodsReturningComplexTypes()
{
    // Methods returning Task<ComplexType> should not be registered as LLM tools
    // because they aren't useful for the LLM and can cause recursive loops
    var agent = Agent(UniqueId("tool-discovery-complex"));
    var metadata = agent.GetMetadata(CancellationToken.None).Result;
    var toolNames = metadata.Tools;

    // FormatResponse returns Task<RichOutput> — should NOT be a tool
    Assert.DoesNotContain("FormatResponse", toolNames);
}
```

- [ ] **Step 2: Add exclusion logic in DiscoverInterfaceTools**

In `src/Core/Agents/Agent.Tools.cs`, inside `DiscoverInterfaceTools`, add after the `IsSpecialName` check:

```csharp
// skip methods returning complex domain types — they aren't useful as LLM tools
// and can cause recursive loops (e.g., FormatResponse calling GetResponse internally)
var returnType = method.ReturnType;
if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
    returnType = returnType.GetGenericArguments()[0];
if (returnType != typeof(Task) && returnType != typeof(string) && returnType != typeof(void)
    && !returnType.IsPrimitive && returnType != typeof(decimal))
    continue;
```

- [ ] **Step 3: Run tool discovery tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ToolDiscovery" -v normal`
Expected: PASS

- [ ] **Step 4: Run all core tests to verify no regressions**

Run: `dotnet test test/Core.Tests -v normal`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Agents/Agent.Tools.cs test/Core.Tests/AgentToolDiscoveryTests.cs
git commit -m "fix: exclude interface methods returning complex types from LLM tool discovery"
```

---

### Task 5: End-to-end verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx -v normal`
Expected: All tests PASS

- [ ] **Step 3: Start Aspire and verify via Telegram**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Send test message via Telegram, verify:
- Short responses: no TelegramUIAgent trace span
- Structured responses (with options): TelegramUIAgent span present, no tool-calling loop
- Token usage in trace should be ~1 LLM call for Thread + conditionally 1 for TelegramUI
- Thread agent input tokens should be significantly lower (20 messages vs 100)
