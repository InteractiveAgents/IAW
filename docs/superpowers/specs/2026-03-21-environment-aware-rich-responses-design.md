# Environment-Aware Rich Responses

**Date:** 2026-03-21
**Status:** Approved

## Problem

When a user sends "Send me 3 jokes and let me select the best" via Telegram, the assistant returns plain text. The Telegram client already supports inline keyboard buttons (approvals, wizards, menus, forms), but the normal conversation response flow (`GetResponseStream` -> `StreamResponseAsync`) is pure text -- there's no mechanism for the LLM to present interactive UI elements during a regular response.

The output layer should be environment-aware and provide the best possible UI for each client.

## Solution: Hybrid AI Tool + Smart Fallback

Two layers working together:

1. **AI Tool (explicit):** Thread agent gets a `PresentOptions` tool the LLM can call to declare interactive options during response generation
2. **Smart Fallback (implicit):** Telegram client detects option-like patterns in text when the LLM forgot to use the tool

### Architecture Constraints

- No changes to `IAgent` interface or `GetResponseStream` contract (`IAsyncEnumerable<string>`)
- No changes to the Orleans streaming/event architecture for this feature
- Options stored temporarily on the Thread grain, consumed by the client after streaming completes
- Each client decides independently how to render (Telegram: inline buttons, DevUI: clickable chips, MCP: text list)
- Each Thread grain has exactly one active client consuming its responses (no multi-client race)

## Detailed Flow

### Phase 1: LLM Streaming with PresentOptions Tool

The Thread agent registers `PresentOptions` as an AI tool via `DefineAdditionalTools()` alongside the existing `Delegate` tool:

```csharp
AIFunctionFactory.Create(PresentOptionsAsync, "PresentOptions",
    "Present interactive options to the user. Use this when offering choices, " +
    "polls, votes, or any scenario where the user should pick from a list.")
```

Tool method signature:
```csharp
Task<string> PresentOptionsAsync(string prompt, string[] options, CancellationToken ct)
```

The LLM passes simple strings for options. The tool internally creates `Option` records with `Label = Value = each string`.

When the LLM calls it mid-stream:

1. Generates a unique callback ID (`opt-{short-guid}`)
2. Stores a `PendingOptions` record in a transient field on the grain (`_pendingOptions`). Note: if the LLM calls the tool multiple times in one response, later calls overwrite earlier ones (last wins).
3. Registers the options in `IUISession` for callback routing
4. Returns text to the LLM: "Options presented to user. Waiting for selection."

The text response continues streaming normally. This is safe from a grain reentrancy perspective because tool calls are awaited inline during `ProduceLlmStreamAsync`, staying on the grain's single-threaded scheduler.

**Transient state note:** `_pendingOptions` is a non-durable field. If the grain deactivates between the tool call and the client consuming options, the field is lost. In practice this window is negligible (the consume call happens immediately after streaming on the same grain call chain), but the client must handle `null` gracefully.

### Phase 2: Post-Stream Button Attachment

After `StreamResponseAsync()` finishes buffering all text:

1. Calls `thread.ConsumePendingOptions(ct)` -- returns the `PendingOptions` and clears it (one-shot)
2. If options exist: builds `InlineKeyboardMarkup` and edits the **same message** with text + buttons
3. If no options from tool: runs smart fallback heuristic on the final text

Result: user sees ONE message with text and inline keyboard buttons.

**Important:** `ConsumePendingOptions` is NOT on the `IThread` interface (methods on leaf interfaces are auto-registered as AI tools by `Agent.Tools.cs`). Instead, it is exposed via a separate `IThreadUI` grain interface that shares the same grain identity as the Thread:

```csharp
public interface IThreadUI : IGrainWithStringKey
{
    Task<PendingOptions?> ConsumePendingOptions(CancellationToken ct);
}
```

`ThreadAgent` implements both `IThread` and `IThreadUI`. The Telegram client calls `clusterClient.GetGrain<IThreadUI>(threadGrainId).ConsumePendingOptions(ct)`.

### Phase 3: Smart Fallback Heuristic

Telegram-only client-side post-processing. Scans the final response text for:

- Numbered list items (`1. ...`, `2. ...`, `3. ...`) near the **end** of the response (not mid-explanation)
- Followed by a question mark or trigger words ("choose", "select", "pick", "vote", "which")

Constraints to avoid false positives:
- Minimum 2 items, maximum 8 items
- Maximum label length: 64 characters (Telegram callback data limit); longer items are skipped
- Only triggers when the numbered list is in the last ~30% of the response text
- Items that are multi-line paragraphs are skipped (only single-line items become buttons)

If detected: extracts labels from numbered items, auto-generates callback ID, registers in `IUISession`, attaches buttons. Same rendering as the explicit tool path.

### Phase 4: Callback Resolution

When user clicks a button:

1. `CallbackQuery` arrives with data `opt:{callbackId}:{value}`
2. `TelegramBotService.HandleCallbackQueryAsync` routes all non-`cmd:` callbacks to `IUISession.HandleCallback()` (existing behavior)
3. `UISession.HandleCallback` gains an `"opt"` branch (consistent with existing `"ap"`, `"wz"`, `"pg"`, `"mn"`, `"fm"` routing)
4. Original message edited: buttons removed, selection confirmed with checkmark
5. Selection injected as a new user message to the Thread with context: `"Re: '{original prompt}' -- I choose: {label}"`
6. New `StreamResponseAsync` cycle starts for this message, with its own placeholder message and message ID

### Phase 5: System Instructions

The Thread agent's instructions are extended:

```
You can present interactive options using the PresentOptions tool.
Use it when offering choices, comparisons, votes, or any pick-one scenario.
The user's environment will render these as clickable buttons.
```

Multi-select is out of scope for this design. Single selection only.

## Data Types

### PendingOptions (new, in Core.Contracts.UI)

```csharp
[GenerateSerializer]
public sealed record PendingOptions(
    [property: Id(0)] string CallbackId,
    [property: Id(1)] string Prompt,
    [property: Id(2)] IReadOnlyList<PendingOption> Options,
    [property: Id(3)] DateTimeOffset ExpiresAt);

[GenerateSerializer]
public sealed record PendingOption(
    [property: Id(0)] string Label,
    [property: Id(1)] string Value);
```

`PendingOption` is a separate type from `Core.UI.Option` to avoid a cross-namespace dependency between `Core.Contracts.UI` and `Core.UI`. It is a simple label+value pair with no optional description.

### IThreadUI (new, in Agents/Orchestration)

```csharp
public interface IThreadUI : IGrainWithStringKey
{
    Task<PendingOptions?> ConsumePendingOptions(CancellationToken ct);
}
```

## Changes by File

| File | Change |
|------|--------|
| `src/Agents/Orchestration/ThreadAgent.cs` | + implement `IThreadUI`, + `PresentOptionsAsync` AI tool in `DefineAdditionalTools()`, + `_pendingOptions` transient field, + `ConsumePendingOptions()` method |
| `src/Agents/Orchestration/IThread.cs` | + updated instructions mentioning PresentOptions |
| `src/Agents/Orchestration/IThreadUI.cs` | + New interface for consuming pending options |
| `src/Core/Contracts/UI/PendingOptions.cs` | + New `PendingOptions` and `PendingOption` records |
| `src/Core/Contracts/IUISession.cs` | + `RegisterOptions()` signature |
| `src/Agents/UI/UISession.cs` | + `RegisterOptions()` implementation, + `"opt"` branch in `HandleCallback` |
| `src/Clients.Telegram/TelegramBotService.cs` | + Post-stream button attachment in `StreamResponseAsync`, + smart fallback heuristic, + feed selection back as new user message with context |

### Files NOT Changed

- `src/Core/Contracts/IAgent.cs` -- no interface changes
- `src/Core/Agents/Agent.cs` -- no streaming contract changes
- `src/Clients.Telegram/StreamSubscriber.cs` -- uses direct grain call, not stream events
- `src/DevUI/OrleansAgentChatClient.cs` -- future enhancement, not in this scope

## Testing Strategy

1. **Unit test:** `PresentOptionsAsync` stores options on grain and returns confirmation text
2. **Unit test:** `ConsumePendingOptions` returns options once, then null on second call
3. **Unit test:** Multiple `PresentOptionsAsync` calls in one response -- last one wins
4. **Unit test:** `IUISession.RegisterOptions` + `HandleCallback` with `opt:` prefix resolves correctly
5. **Unit test:** Smart fallback heuristic parses numbered lists correctly (positive cases)
6. **Unit test:** Smart fallback does NOT trigger for mid-text numbered lists, long paragraphs, or lists > 8 items (negative cases)
7. **Unit test:** Fallback skips items with labels > 64 characters
8. **Integration test:** Send message via Thread agent with mocked LLM that calls PresentOptions tool, verify `ConsumePendingOptions` returns correct data

## Success Criteria

- User sends "give me 3 jokes and let me pick" via Telegram -> sees one message with jokes text AND inline buttons
- Clicking a button removes buttons, confirms selection, and the assistant continues the conversation with context
- If the LLM forgets to use PresentOptions, the smart fallback still generates buttons from numbered lists at the end of responses
- No changes to how DevUI or MCP consume responses (they just get text for now)
