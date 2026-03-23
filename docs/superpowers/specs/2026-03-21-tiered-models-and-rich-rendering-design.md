# Tiered Model Abstraction + Rich Response Rendering

**Date:** 2026-03-21
**Status:** Approved

## Problem

Two related gaps:

1. **Agents are tied to concrete models.** `[Llm<Opus46>]` hardcodes the model. Swapping Haiku for GPT-4o-mini means touching every agent that uses it. Agents should request a capability tier (fast/balanced/reasoning), and the AppHost decides which concrete model fulfills each tier.

2. **Responses are plain text in Telegram.** The assistant outputs raw text with no formatting, no buttons, no suggested actions. The output layer should be environment-aware and produce the best possible UX. A dedicated `TelegramUIAgent` should transform raw responses into rich Telegram output (MarkdownV2 formatting, inline buttons, suggestions, media).

## Solution Overview

**Phase 1: Tiered Model Abstraction** -- Three abstract model tiers (`Fast`, `Balanced`, `Reasoning`) that agents request via `[Llm<Fast>]`. The AppHost maps concrete models to tiers via `.AsFast()`, `.AsBalanced()`, `.AsReasoning()`.

**Phase 2: TelegramUIAgent + Rich Rendering** -- A dedicated agent that takes raw response text and outputs formatted text + `List<UIPart>` for the Telegram client. Replaces both the `PresentOptions` tool on ThreadAgent and the `OptionsFallbackDetector` heuristic. Uses `[Llm<Fast>]` since it needs quick, cheap formatting.

---

## Phase 1: Tiered Model Abstraction

### Concept

```csharp
// AppHost configuration
iaw.WithLLM<Opus46>().AsReasoning()
   .WithLLM<Sonnet46>().AsBalanced()
   .WithLLM<Haiku45>().AsFast()

// Agent usage -- request by tier
[Llm<Fast>] IChatClient chatClient
[Llm<Balanced>] IChatClient chatClient
[Llm<Reasoning>] IChatClient chatClient

// Concrete model still works (for model-wrapper agents like Gpt54MiniAgent)
[Llm<Opus46>] IChatClient chatClient
```

### Data Model

Three marker types in `src/Core/AI/ModelTiers.cs`:

```csharp
public sealed class Fast : LLMModel
{
    internal Fast() : base("tier-fast", "tier", "Fast") { }
}
public sealed class Balanced : LLMModel
{
    internal Balanced() : base("tier-balanced", "tier", "Balanced") { }
}
public sealed class Reasoning : LLMModel
{
    internal Reasoning() : base("tier-reasoning", "tier", "Reasoning") { }
}
```

These extend `LLMModel` with a synthetic `Id` / `Provider` / `DisplayName` so they survive auto-discovery in `LLMModel.All` (which calls `Activator.CreateInstance()` on all non-abstract, non-nested subclasses and accesses `ServiceKey` which calls `Id.ToLowerInvariant()`). Their `Provider` is `"tier"` so the registration pipeline can distinguish them from concrete models.

Their computed `ServiceKey` values are: `tier-tier-fast`, `tier-tier-balanced`, `tier-tier-reasoning`. The `LlmAttribute<Fast>` constructor finds the `Fast` instance via `LLMModel.All.FirstOrDefault(m => m.GetType() == typeof(Fast))` and resolves its `ServiceKey` -- this works because the tier types are in `LLMModel.All` with valid identities.

### AppHost Integration

`WithLLM<T>()` currently returns `IAWService` directly. To support `.AsFast()` chaining without ambiguous stateful calls, `WithLLM<T>()` returns a new `LLMModelBuilder` that exposes tier assignment:

```csharp
// In IAWService
public LLMModelBuilder WithLLM<T>() where T : LLMModel { ... }

// New intermediate builder
public sealed class LLMModelBuilder
{
    // all existing IAWService methods forwarded so the chain continues
    public LLMModelBuilder WithLLM<T>() where T : LLMModel { ... }
    public IAWService AsFast() { ... }
    public IAWService AsBalanced() { ... }
    public IAWService AsReasoning() { ... }
    // implicit conversion to IAWService for chains that don't assign a tier
}
```

This makes `.AsFast()` type-safe: it can only follow `.WithLLM<T>()`. Calling `.AsFast().AsFast()` is impossible because `.AsFast()` returns `IAWService`, not `LLMModelBuilder`.

Each `.As*()` method records a tier mapping: `{ TierServiceKey -> ConcreteModelServiceKey }`. These mappings are stored on `IAWService` and propagated to projects via env vars.

### Environment Propagation

`.WithReference(iaw)` already propagates model config as env vars (`AI__LLM__Models__N__*`). Tier assignments are propagated as additional env vars:

```
AI__LLM__Tiers__Fast=<ServiceKey of assigned concrete model>
AI__LLM__Tiers__Balanced=<ServiceKey of assigned concrete model>
AI__LLM__Tiers__Reasoning=<ServiceKey of assigned concrete model>
```

### Silo Registration

In `LlmRegistration.AddLlmProviders()` (in `src/Aspire.IAW.Client/LlmRegistration.cs`), after registering all concrete model `IChatClient` entries:

1. Read `AI__LLM__Tiers__*` env vars
2. For each tier, register a keyed `IChatClient` alias:

```csharp
services.AddKeyedSingleton<IChatClient>(
    tierModel.ServiceKey,
    (sp, _) => sp.GetRequiredKeyedService<IChatClient>(concreteModelServiceKey));
```

3. Skip tier types (where `Provider == "tier"`) when creating concrete model chat clients -- they are aliases only, not real models.

### Fallback Behavior

If no model is assigned to a tier via `.As*()`, the tier env var is not set. At silo startup, if a tier env var is missing, the tier's `IChatClient` is aliased to the default model (first registered concrete model). This is a soft fallback, not an error.

### What Changes

| File | Change |
|------|--------|
| `src/Core/AI/ModelTiers.cs` | New: `Fast`, `Balanced`, `Reasoning` with synthetic identity (`"tier"` provider) |
| `src/Aspire.Hosting.IAW/IAWService.cs` | `WithLLM<T>()` returns `LLMModelBuilder`; add `.AsFast()`, `.AsBalanced()`, `.AsReasoning()`; propagate tier env vars |
| `src/Aspire.Hosting.IAW/LLMModelBuilder.cs` | New: intermediate builder for type-safe tier chaining |
| `src/Aspire.IAW.Client/LlmRegistration.cs` | Read tier env vars, register keyed `IChatClient` aliases, skip `Provider == "tier"` in concrete model creation |
| `src/IAW.AppHost/AppHost.cs` | Use new tier syntax |

### Testing

- `[Llm<Fast>]` resolves to the model configured with `.AsFast()`
- `[Llm<Reasoning>]` resolves to the model configured with `.AsReasoning()`
- Concrete `[Llm<Opus46>]` still works alongside tiers
- If no tier is assigned, falls back to default model
- Tier types in `LLMModel.All` have valid `ServiceKey` and don't crash auto-discovery
- `LlmAttribute<Fast>` constructor finds `Fast` in `LLMModel.All` and resolves `ServiceKey`
- Tier assignment propagates through `WithReference(iaw)` env vars
- Mock `IChatClient` in `AgentTest<T>` is registered for tier service keys (via `RegisterAllAttributeMappers`)
- `.AsFast()` can only follow `.WithLLM<T>()` (type-safe chaining)

---

## Phase 2: TelegramUIAgent + Rich Rendering

### Concept

A dedicated agent that transforms raw assistant responses into rich Telegram output. It is to Telegram UX what `RoslynAgent` is to C# -- a domain expert.

### Architecture

```
Thread agent streams response (pure content, no UX tools)
    ↓
Text complete, Telegram client has final text
    ↓
Telegram client calls TelegramUIAgent.FormatResponse(text)
    ↓
TelegramUIAgent ([Llm<Fast>], StatelessWorker):
  - Expert system prompt about Telegram Bot API capabilities
  - Uses tool-call mode with a single format_response tool for reliable structured output
  - Knows: MarkdownV2 escaping, InlineKeyboardButton, button styles,
    callback_data 64-byte limit, media message rules, forum topics
  - Analyzes response text
  - Returns: RichOutput (formatted text + List<UIPart>)
    ↓
Telegram client renders:
  - Formatted text via editMessageText with MarkdownV2
  - Option buttons as InlineKeyboardMarkup (pick-one, removes on click)
  - Suggestion buttons as InlineKeyboardMarkup (different row, "primary" style)
  - Media as separate sendPhoto/sendDocument messages

On failure: falls back to plain text (current behavior)
```

### The Agent

```csharp
public interface ITelegramUI : IAgent
{
    Task<RichOutput> FormatResponse(string rawText, CancellationToken ct);
}
```

`RichOutput` is a simple record:
```csharp
[GenerateSerializer]
public sealed record RichOutput(
    [property: Id(0)] string FormattedText,
    [property: Id(1)] IReadOnlyList<UIPart> Parts);
```

The agent uses `[Llm<Fast>]` and has a detailed system prompt covering Telegram Bot API capabilities. It uses **tool-call mode** with a single `format_response` tool to ensure reliable structured output from cheap models. The tool schema matches the `RichOutput` structure.

The agent is a **`[StatelessWorker]`** grain -- it performs a stateless transformation (text in, formatted output out) and does not need durable state. This allows Orleans to scale it across silos.

The agent lives in `src/Agents` (not `src/Clients.Telegram`) because it's an Orleans grain that runs on the silo.

### System Instructions (summary)

The TelegramUIAgent's instructions cover:
- Convert standard markdown (`**bold**`, `_italic_`, `` `code` ``, `[link](url)`) to MarkdownV2
- Detect when options/choices are presented and generate `OptionsPart`
- Detect suggested next actions and generate `SuggestionPart`
- Detect media references and generate `MediaPart`
- Know button style options: "danger" (red), "success" (green), "primary" (blue)
- Know callback_data 64-byte limit -- use short numeric index values (e.g., "1", "2", "3")
- Know that photos/documents must be separate messages
- Only generate `CardPart`, `FormPart`, `ProgressPart` if clearly applicable -- these existing UIPart types are in scope but secondary

### UIPart Extensions

Add one new type to the existing `UIPart` hierarchy:

```csharp
[GenerateSerializer]
public record SuggestionPart(
    [property: Id(0)] string CallbackId,
    [property: Id(1)] IReadOnlyList<SuggestedAction> Actions) : UIPart;

[GenerateSerializer]
public record SuggestedAction(
    [property: Id(0)] string Label,
    [property: Id(1)] string ActionText);
```

`SuggestionPart` has its own `SuggestedAction` type (not `PendingOption`) to cleanly separate the agent's output contract from UISession's internal callback state. The Telegram client maps `SuggestedAction` to `PendingOption` during UISession registration.

`SuggestionPart` is like `OptionsPart` but with different click behavior: instead of confirming selection and removing buttons, it sends the action text as a new user message.

### Telegram Client Changes

`StreamResponseAsync` simplifies:

1. Stream text as before (plain text edits during streaming for real-time feedback)
2. After streaming completes, call `ITelegramUI.FormatResponse(finalText)`
3. On success, render `RichOutput`:
   - Edit the message with `FormattedText` + MarkdownV2 parse mode
   - For `OptionsPart`: attach InlineKeyboardMarkup, register in UISession
   - For `SuggestionPart`: attach InlineKeyboardMarkup (separate row, "primary" style), register in UISession with type `"suggestion"`
   - For `MediaPart`: send as separate messages via `sendPhoto`/`sendDocument`
4. **On failure** (API error, timeout, malformed output): fall back to `EditSafe` with plain text -- the user still sees the response, just without rich formatting

### Callback Handling

Both `OptionsPart` and `SuggestionPart` use the same `opt:` callback prefix and UISession registration. The `PendingOptionSet` in UISession gains a `Type` field to distinguish behavior in `HandleCallbackQueryAsync`:

- Type `"option"`: edit message (confirm selection with checkmark, remove buttons)
- Type `"suggestion"`: send action text as new user message to Thread, stream response

Callback data format: `opt:{callbackId}:{index}` where index is 1-based numeric. CallbackId is `opt-{8chars}`. Total: ~20 bytes, well within 64-byte limit.

### What Gets Removed

- `PresentOptions` tool from ThreadAgent -- content LLM no longer does UX
- `OptionsFallbackDetector` -- replaced by TelegramUIAgent intelligence
- `IThreadUI` / `ConsumePendingOptions` -- no longer needed, UI agent is called directly by client
- `_pendingOptions` field on ThreadAgent -- no longer needed

### What Changes

| File | Change |
|------|--------|
| `src/Agents/Orchestration/TelegramUIAgent.cs` | New: `[StatelessWorker]` agent with `[Llm<Fast>]` and Telegram-expert prompt |
| `src/Agents/Orchestration/ITelegramUI.cs` | New: interface with `FormatResponse` |
| `src/Core/UI/UIPart.cs` | Add `SuggestionPart` and `SuggestedAction` records |
| `src/Core/UI/RichOutput.cs` | New: `RichOutput` record (FormattedText + List of UIPart) |
| `src/Core/Contracts/UI/PendingOptions.cs` | Add `Type` field to `PendingOptionSet` |
| `src/Clients.Telegram/TelegramBotService.cs` | Replace post-stream logic: call TelegramUIAgent, render RichOutput with plaintext fallback, handle suggestion callbacks |
| `src/Agents/Orchestration/ThreadAgent.cs` | Remove `PresentOptions` tool, `_pendingOptions`, `IThreadUI` impl |
| `src/Agents/Orchestration/IThreadUI.cs` | Remove (replaced by ITelegramUI) |
| `src/Agents/Orchestration/OptionsFallbackDetector.cs` | Remove (replaced by TelegramUIAgent) |
| `src/Agents/UI/UISession.cs` | Add `Type` field handling in `opt:` callback branch |

### Testing

- TelegramUIAgent converts `**bold**` text to MarkdownV2 `*bold*` with proper escaping
- TelegramUIAgent detects numbered list + question and returns `OptionsPart`
- TelegramUIAgent detects suggested next actions and returns `SuggestionPart`
- `SuggestionPart` callback sends action text as new message to Thread
- `OptionsPart` callback confirms selection and removes buttons (existing behavior)
- `[Llm<Fast>]` on TelegramUIAgent resolves correctly
- Fallback: when `FormatResponse` throws, client renders plain text without error
- Integration: send message via Telegram, verify formatted text + buttons rendered

---

## Implementation Order

1. **Phase 1: Tiered Models** -- implement `Fast`/`Balanced`/`Reasoning`, wire AppHost, verify existing agents unaffected
2. **Phase 2: TelegramUIAgent** -- depends on Phase 1 (needs `[Llm<Fast>]`), implement agent + client changes, remove old PresentOptions/fallback code

## Success Criteria

- Agents use `[Llm<Fast>]` / `[Llm<Balanced>]` / `[Llm<Reasoning>]` and resolve to configured models
- Concrete `[Llm<Opus46>]` still works unchanged
- Telegram messages render with proper formatting (bold, italic, code, links)
- Telegram messages show inline buttons for choices and suggested actions
- Clicking a suggestion button sends it as a message and continues conversation
- Zero error spans in traces for normal message flow
- When TelegramUIAgent fails, response still shows as plain text
