# TelegramUIAgent + Rich Response Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A dedicated TelegramUIAgent that transforms raw LLM responses into rich Telegram output with MarkdownV2 formatting, inline buttons, and suggested actions -- replacing the PresentOptions tool and OptionsFallbackDetector.

**Architecture:** After the Thread agent streams its response, the Telegram client calls `ITelegramUI.FormatResponse(text)` on a `[StatelessWorker]` agent with `[Llm<Fast>]`. The agent returns `RichOutput` (formatted text + `List<UIPart>`). The client renders MarkdownV2 text, option buttons, suggestion buttons, and media. On failure, falls back to plain text.

**Tech Stack:** Orleans grains, Microsoft.Extensions.AI, Telegram BotAPI, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-21-tiered-models-and-rich-rendering-design.md` (Phase 2)

**Prerequisite:** Phase 1 (Tiered Models) must be implemented first -- this plan uses `[Llm<Fast>]`.

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Core/UI/RichOutput.cs` | Create | `RichOutput` record (FormattedText + List of UIPart) |
| `src/Core/UI/UIPart.cs` | Modify | Add `SuggestionPart` and `SuggestedAction` records |
| `src/Core/Contracts/UI/PendingOptions.cs` | Modify | Add `Type` field to `PendingOptionSet` |
| `src/Agents/Orchestration/ITelegramUI.cs` | Create (replace) | New interface with `FormatResponse` (replaces old `IThreadUI`) |
| `src/Agents/Orchestration/TelegramUIAgent.cs` | Create | `[StatelessWorker]` agent with `[Llm<Fast>]` and Telegram-expert prompt |
| `src/Agents/UI/UISession.cs` | Modify | Handle `Type` field in `opt:` callback branch for suggestion vs option |
| `src/Clients.Telegram/TelegramBotService.cs` | Modify | Replace post-stream logic: call TelegramUIAgent, render RichOutput, fallback |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Remove `PresentOptionsAsync` tool, `_pendingOptions` field, `IThreadUI` impl |
| `src/Agents/Orchestration/IThread.cs` | Modify | Remove PresentOptions from instructions |
| `src/Agents/Orchestration/IThreadUI.cs` | Remove | Replaced by ITelegramUI |
| `src/Agents/Orchestration/OptionsFallbackDetector.cs` | Remove | Replaced by TelegramUIAgent intelligence |
| `test/Core.Tests/UI/SuggestionPartTests.cs` | Create | Tests for SuggestionPart and UISession type handling |
| `test/Core.Tests/TelegramUIAgentTests.cs` | Create | Tests for TelegramUIAgent FormatResponse |
| `test/Core.Tests/ThreadOptionsTests.cs` | Remove | No longer needed (PresentOptions removed) |
| `test/Core.Tests/OptionsFallbackTests.cs` | Remove | No longer needed (detector removed) |

---

### Task 1: RichOutput and SuggestionPart Data Types

**Files:**
- Create: `src/Core/UI/RichOutput.cs`
- Modify: `src/Core/UI/UIPart.cs`

- [ ] **Step 1: Create RichOutput record**

Create `src/Core/UI/RichOutput.cs`:

```csharp
namespace Core.UI;

[GenerateSerializer]
public sealed record RichOutput(
    [property: Id(0)] string FormattedText,
    [property: Id(1)] IReadOnlyList<UIPart> Parts);
```

- [ ] **Step 2: Add SuggestionPart and SuggestedAction to UIPart.cs**

Read `src/Core/UI/UIPart.cs`. Append after the last record (before closing of file):

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

- [ ] **Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Core/UI/RichOutput.cs src/Core/UI/UIPart.cs
git commit -m "feat: add RichOutput record and SuggestionPart UIPart type"
```

---

### Task 2: UISession Type-Aware Callback Handling

**Files:**
- Modify: `src/Core/Contracts/UI/PendingOptions.cs`
- Modify: `src/Agents/UI/UISession.cs`
- Modify: `src/Core/Contracts/IUISession.cs`
- Create: `test/Core.Tests/UI/SuggestionPartTests.cs`

- [ ] **Step 1: Write failing tests**

Create `test/Core.Tests/UI/SuggestionPartTests.cs`:

```csharp
using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class SuggestionTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .UseInMemoryReminderService();
        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();
        siloBuilder.Services.AddSingleton<IAttributeToFactoryMapper<UISessionStateAttribute>, UISessionStateMapper>();
    }
}

public class SuggestionPartTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SuggestionTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    [Fact]
    public async Task RegisterOptions_WithSuggestionType_HandleCallback_ReturnsTypeInAction()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("sug-user-1");

        var options = new[]
        {
            new PendingOption("Another 5 rounds", "1"),
            new PendingOption("Final pick", "2")
        };
        await session.RegisterOptions("sug-abc123", "What next?", options, "proj/slug", "suggestion", ct);

        var result = await session.HandleCallback("sug-abc123", "opt:sug-abc123:1", ct);

        Assert.Contains("Another 5 rounds", result.NewText);
        Assert.Equal("suggestion:1", result.Action);
    }

    [Fact]
    public async Task RegisterOptions_WithOptionType_HandleCallback_ReturnsPlainAction()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("sug-user-2");

        var options = new[] { new PendingOption("Joke A", "1") };
        await session.RegisterOptions("opt-xyz", "Pick one", options, "proj", "option", ct);

        var result = await session.HandleCallback("opt-xyz", "opt:opt-xyz:1", ct);

        Assert.Equal("1", result.Action);
        Assert.DoesNotContain("suggestion:", result.Action ?? "");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~SuggestionPartTests" -v m`
Expected: FAIL -- `RegisterOptions` does not accept `type` parameter

- [ ] **Step 3: Add Type field to PendingOptionSet**

In `src/Core/Contracts/UI/PendingOptions.cs`, modify `PendingOptionSet` to add a `Type` field:

```csharp
[GenerateSerializer]
public sealed record PendingOptionSet(
    [property: Id(0)] string Id,
    [property: Id(1)] string Prompt,
    [property: Id(2)] IReadOnlyList<PendingOption> Options,
    [property: Id(3)] string ProjectSlug,
    [property: Id(4)] DateTimeOffset CreatedAt,
    [property: Id(5)] string Type = "option");
```

- [ ] **Step 4: Add type parameter to IUISession.RegisterOptions**

In `src/Core/Contracts/IUISession.cs`, update the `RegisterOptions` signature to include `type`:

```csharp
Task RegisterOptions(string optionsId, string prompt, PendingOption[] options, string projectSlug, string type, CancellationToken ct);
```

- [ ] **Step 5: Update UISession.RegisterOptions and HandleCallback**

In `src/Agents/UI/UISession.cs`:

Update `RegisterOptions` to pass the type:
```csharp
public Task RegisterOptions(string optionsId, string prompt, PendingOption[] options, string projectSlug, string type, CancellationToken ct)
{
    state.PendingOptionSets[optionsId] = new PendingOptionSet(
        optionsId, prompt, options, projectSlug, DateTimeOffset.UtcNow, type);
    return Task.CompletedTask;
}
```

Update the `"opt"` branch in `HandleCallback` to include the type in the action:
```csharp
if (type == "opt" && state.PendingOptionSets.TryGetValue(id, out var optionSet))
{
    var selectedOption = optionSet.Options.FirstOrDefault(o => o.Value == action);
    var label = selectedOption?.Label ?? action;
    state.PendingOptionSets.Remove(id);

    var actionValue = optionSet.Type == "suggestion"
        ? $"suggestion:{action}"
        : action;

    return new CallbackResult(
        $"\u2705 {optionSet.Prompt} \u2014 {label}", actionValue, null);
}
```

- [ ] **Step 6: Fix existing callers of RegisterOptions**

The existing callers in `TelegramBotService.cs` (fallback detector path) and `ThreadAgent.cs` (`PresentOptionsAsync`) call `RegisterOptions` without the `type` parameter. Add `"option"` as the type argument to all existing call sites. Search for `RegisterOptions(` and update each call.

- [ ] **Step 7: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~SuggestionPartTests" -v m`
Expected: All 2 tests PASS

Then: `dotnet test test/Core.Tests -v m`
Expected: All tests pass (including existing OptionsTests)

- [ ] **Step 8: Commit**

```bash
git add src/Core/Contracts/UI/PendingOptions.cs src/Core/Contracts/IUISession.cs src/Agents/UI/UISession.cs src/Clients.Telegram/TelegramBotService.cs src/Agents/Orchestration/ThreadAgent.cs test/Core.Tests/UI/SuggestionPartTests.cs
git commit -m "feat: add type-aware callback handling for option vs suggestion"
```

---

### Task 3: ITelegramUI Interface and TelegramUIAgent

**Files:**
- Create: `src/Agents/Orchestration/ITelegramUI.cs`
- Create: `src/Agents/Orchestration/TelegramUIAgent.cs`
- Create: `test/Core.Tests/TelegramUIAgentTests.cs`

- [ ] **Step 1: Write failing tests**

Create `test/Core.Tests/TelegramUIAgentTests.cs`:

```csharp
using Core.UI;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class TelegramUIAgentTests : AgentTest<TelegramUIAgent>
{
    [Fact]
    public async Task FormatResponse_ReturnsRichOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Cluster.Client.GetGrain<ITelegramUI>(UniqueId("fmt"));

        var result = await agent.FormatResponse("Hello world", ct);

        Assert.NotNull(result);
        Assert.NotEmpty(result.FormattedText);
    }

    [Fact]
    public async Task FormatResponse_EmptyText_ReturnsEmptyParts()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Cluster.Client.GetGrain<ITelegramUI>(UniqueId("empty"));

        var result = await agent.FormatResponse("", ct);

        Assert.NotNull(result);
        Assert.Empty(result.Parts);
    }
}
```

Note: With `MockChatClient` returning `"mock-response"`, the agent won't produce real MarkdownV2 or detect options. These tests verify the plumbing works -- the agent activates, calls the LLM, returns `RichOutput`. Real formatting quality is tested via integration.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~TelegramUIAgentTests" -v m`
Expected: FAIL -- `ITelegramUI` and `TelegramUIAgent` don't exist

- [ ] **Step 3: Create ITelegramUI interface**

Create `src/Agents/Orchestration/ITelegramUI.cs`:

```csharp
using Core.Contracts;
using Core.UI;

namespace IAW.Agents.Orchestration;

public interface ITelegramUI : IAgent
{
    static string IAgent.AgentDisplayName => "Telegram UI";

    static string IAgent.AgentDescription =>
        "Formats raw assistant responses into rich Telegram output with MarkdownV2, inline buttons, and suggested actions.";

    static string[] IAgent.AgentCapabilities =>
        ["formatting", "telegram", "ui", "markdown"];

    static string IAgent.AgentInstructions => """
        You are a Telegram UX formatting specialist. You receive raw assistant response
        text and transform it into the best possible Telegram experience.

        Your job is to output a JSON object with two fields:
        - formattedText: the response converted to Telegram MarkdownV2 format
        - parts: an array of UI parts (options, suggestions, media)

        MARKDOWNV2 RULES:
        - Bold: *text* (escape literal * with \*)
        - Italic: _text_ (escape literal _ with \_)
        - Underline: __text__
        - Strikethrough: ~text~
        - Code: `code` or ```language\ncode```
        - Links: [text](url)
        - These chars MUST be escaped outside formatting: _ * [ ] ( ) ~ ` > # + - = | { } . !

        UI PARTS you can generate:
        - options: when the response presents choices to pick from (numbered lists, alternatives).
          Each option has label (display text) and value (short index "1","2","3").
        - suggestions: when natural follow-up actions exist ("continue", "show more", "start over").
          Each suggestion has label (button text) and actionText (message to send).
        - media: when the response references downloadable files or images.

        RULES:
        - Keep formattedText faithful to the original meaning
        - Only generate options/suggestions when clearly appropriate
        - Button labels max 40 chars
        - Suggestions should be concise natural next steps, not repeating the full response
        - Max 8 options, max 4 suggestions
        - If the response is simple (greeting, short answer), return empty parts array
        """;

    Task<RichOutput> FormatResponse(string rawText, CancellationToken ct);
}
```

- [ ] **Step 4: Create TelegramUIAgent**

Create `src/Agents/Orchestration/TelegramUIAgent.cs`:

```csharp
using Core;
using Core.Contracts;
using Core.UI;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IAW.Agents.Orchestration;

[StatelessWorker]
[GrainType("telegram-ui")]
public class TelegramUIAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient,
    ILogger<TelegramUIAgent> logger)
    : Agent<ITelegramUI>(durableState, chatClient), ITelegramUI
{
    public async Task<RichOutput> FormatResponse(string rawText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new RichOutput("", []);

        try
        {
            var response = await GetResponse(
                $"Format this response for Telegram. Return ONLY valid JSON matching the schema.\n\nRESPONSE TEXT:\n{rawText}", ct);

            return ParseRichOutput(response, rawText);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TelegramUI formatting failed, returning plain text");
            return new RichOutput(rawText, []);
        }
    }

    static RichOutput ParseRichOutput(string llmResponse, string fallbackText)
    {
        try
        {
            var jsonStart = llmResponse.IndexOf('{');
            var jsonEnd = llmResponse.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0)
                return new RichOutput(fallbackText, []);

            var json = llmResponse[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var formattedText = root.TryGetProperty("formattedText", out var ft)
                ? ft.GetString() ?? fallbackText
                : fallbackText;

            var parts = new List<UIPart>();

            if (root.TryGetProperty("parts", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in partsEl.EnumerateArray())
                {
                    if (!part.TryGetProperty("type", out var typeEl)) continue;
                    var type = typeEl.GetString();

                    if (type == "options" && part.TryGetProperty("items", out var items))
                    {
                        var callbackId = $"opt-{Guid.NewGuid().ToString("N")[..8]}";
                        var prompt = part.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
                        var options = new List<Option>();
                        var index = 1;
                        foreach (var item in items.EnumerateArray())
                        {
                            var label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                            if (label.Length > 0)
                                options.Add(new Option(label, index.ToString()));
                            index++;
                        }
                        if (options.Count >= 2)
                            parts.Add(new OptionsPart(prompt, options, callbackId));
                    }

                    if (type == "suggestions" && part.TryGetProperty("items", out var sugItems))
                    {
                        var callbackId = $"sug-{Guid.NewGuid().ToString("N")[..8]}";
                        var actions = new List<SuggestedAction>();
                        foreach (var item in sugItems.EnumerateArray())
                        {
                            var label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                            var actionText = item.TryGetProperty("actionText", out var a) ? a.GetString() ?? label : label;
                            if (label.Length > 0)
                                actions.Add(new SuggestedAction(label, actionText));
                        }
                        if (actions.Count > 0)
                            parts.Add(new SuggestionPart(callbackId, actions));
                    }
                }
            }

            return new RichOutput(formattedText, parts);
        }
        catch
        {
            return new RichOutput(fallbackText, []);
        }
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~TelegramUIAgentTests" -v m`
Expected: All 2 tests PASS

- [ ] **Step 6: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded (file lock errors OK)

- [ ] **Step 7: Commit**

```bash
git add src/Agents/Orchestration/ITelegramUI.cs src/Agents/Orchestration/TelegramUIAgent.cs test/Core.Tests/TelegramUIAgentTests.cs
git commit -m "feat: add TelegramUIAgent with StatelessWorker and Fast model"
```

---

### Task 4: Telegram Client Integration

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

This replaces the post-stream logic to call TelegramUIAgent and render RichOutput.

- [ ] **Step 1: Read the current TelegramBotService.cs**

Read the full file to understand current state after the PresentOptions changes.

- [ ] **Step 2: Replace StreamResponseAsync post-stream logic**

After the streaming loop (after `var finalText = buffer.ToString();`), replace the existing `ConsumePendingOptions` / `OptionsFallbackDetector` logic with:

```csharp
// call TelegramUIAgent to format response
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
    else if (finalText.Length > 0)
    {
        await EditSafe(chatId, currentMessageId, finalText);
    }
}
catch (Exception ex)
{
    logger.LogWarning(ex, "TelegramUI formatting failed for user {TelegramId}, falling back to plain text", telegramId);
    if (finalText.Length > 0)
        await EditSafe(chatId, currentMessageId, finalText);
}
```

- [ ] **Step 3: Add RenderRichOutput method**

```csharp
private async Task RenderRichOutput(long chatId, int messageId, int? topicId, RichOutput richOutput, long telegramId, CancellationToken ct)
{
    var userId = telegramId.ToString();
    var threadId = $"{telegramId}/{topicId?.ToString() ?? "general"}";
    var keyboard = BuildKeyboard(richOutput.Parts, userId, threadId, ct);

    if (keyboard is not null)
    {
        await EditWithMarkdown(chatId, messageId, richOutput.FormattedText, keyboard);
    }
    else
    {
        await EditWithMarkdown(chatId, messageId, richOutput.FormattedText);
    }

    // send media as separate messages
    foreach (var part in richOutput.Parts.OfType<MediaPart>())
    {
        try
        {
            if (part.MimeType.StartsWith("image/"))
                await botClient.SendPhotoAsync(chatId, new Telegram.BotAPI.AvailableTypes.InputFile(part.Url),
                    messageThreadId: topicId, caption: part.Caption);
            else
                await botClient.SendDocumentAsync(chatId, new Telegram.BotAPI.AvailableTypes.InputFile(part.Url),
                    messageThreadId: topicId, caption: part.Caption);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send media {FileName}", part.FileName);
        }
    }
}

private InlineKeyboardMarkup? BuildKeyboard(IReadOnlyList<UIPart> parts, string userId, string threadId, CancellationToken ct)
{
    var rows = new List<InlineKeyboardButton[]>();
    var session = clusterClient.GetGrain<IUISession>(userId);

    foreach (var part in parts)
    {
        if (part is OptionsPart options && options.Options.Count >= 2)
        {
            var pendingOptions = options.Options.Select(o => new PendingOption(o.Label, o.Value)).ToArray();
            session.RegisterOptions(options.CallbackId, options.Prompt, pendingOptions, threadId, "option", ct)
                .Ignore();
            rows.Add(options.Options.Select(o =>
                new InlineKeyboardButton(o.Label) { CallbackData = $"opt:{options.CallbackId}:{o.Value}" }
            ).ToArray());
        }

        if (part is SuggestionPart suggestions)
        {
            var pendingOptions = suggestions.Actions.Select(a => new PendingOption(a.Label, a.ActionText)).ToArray();
            session.RegisterOptions(suggestions.CallbackId, "", pendingOptions, threadId, "suggestion", ct)
                .Ignore();
            rows.Add(suggestions.Actions.Select((a, i) =>
                new InlineKeyboardButton(a.Label)
                {
                    CallbackData = $"opt:{suggestions.CallbackId}:{i + 1}",
                    Style = "primary"
                }
            ).ToArray());
        }
    }

    return rows.Count > 0 ? new InlineKeyboardMarkup([.. rows]) : null;
}
```

- [ ] **Step 4: Add EditWithMarkdown method**

```csharp
private async Task EditWithMarkdown(long chatId, int messageId, string markdownText, InlineKeyboardMarkup? keyboard = null)
{
    if (string.IsNullOrWhiteSpace(markdownText)) return;
    try
    {
        await botClient.EditMessageTextAsync(chatId, messageId, markdownText,
            parseMode: FormatStyles.MarkdownV2, replyMarkup: keyboard);
    }
    catch (BotRequestException)
    {
        // MarkdownV2 failed, try plain text
        try
        {
            await botClient.EditMessageTextAsync(chatId, messageId, markdownText,
                replyMarkup: keyboard);
        }
        catch (BotRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
        }
    }
}
```

- [ ] **Step 5: Update HandleCallbackQueryAsync for suggestion type**

In `HandleCallbackQueryAsync`, update the `opt:` callback handling block to detect suggestion type:

```csharp
if (callbackQuery.Data?.StartsWith("opt:") == true && result.Action is not null)
{
    if (result.Action.StartsWith("suggestion:"))
    {
        // suggestion: send the action text as a new message
        var suggestionIndex = result.Action["suggestion:".Length..];
        var selectedLabel = result.NewText?.Contains('\u2014') == true
            ? result.NewText.Split('\u2014', 2).Last().Trim()
            : suggestionIndex;

        var from = callbackQuery.From;
        var topicId = (callbackQuery.Message as Message)?.MessageThreadId;
        var (thread, _) = await ResolveThreadAsync(from.Id, topicId, ct);
        var selectionMessage = BuildChatMessage(selectedLabel);

        var sent = await botClient.SendMessageAsync(chatId, "...", messageThreadId: topicId);
        await StreamResponseAsync(chatId, sent.MessageId, topicId, thread, selectionMessage, from.Id, ct);
    }
}
```

- [ ] **Step 6: Add required usings**

Add to top of `TelegramBotService.cs`:
```csharp
using Core.UI;
```

- [ ] **Step 7: Build**

Run: `dotnet build src/Clients.Telegram/Telegram.csproj`
Expected: Build succeeded (file lock errors OK)

- [ ] **Step 8: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: wire TelegramUIAgent into Telegram client with RichOutput rendering"
```

---

### Task 5: Remove Old PresentOptions Infrastructure

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`
- Modify: `src/Agents/Orchestration/IThread.cs`
- Remove: `src/Agents/Orchestration/IThreadUI.cs`
- Remove: `src/Agents/Orchestration/OptionsFallbackDetector.cs`
- Remove: `test/Core.Tests/ThreadOptionsTests.cs`
- Remove: `test/Core.Tests/OptionsFallbackTests.cs`

- [ ] **Step 1: Remove PresentOptions from ThreadAgent**

Read `src/Agents/Orchestration/ThreadAgent.cs`. Remove:
- `IThreadUI` from the class declaration (keep `IThread` only)
- `using Core.Contracts.UI;` (if no longer needed)
- `_pendingOptions` field
- `PresentOptionsAsync` method
- `ConsumePendingOptions` method
- The `PresentOptions` entry from `DefineAdditionalTools()` (keep only `Delegate`)

- [ ] **Step 2: Remove PresentOptions from IThread instructions**

Read `src/Agents/Orchestration/IThread.cs`. Remove the PresentOptions bullet from `AgentInstructions`:
```
        - Use the PresentOptions tool when: offering choices, comparisons,
          votes, polls, or any scenario where the user should pick from a list.
          The user's environment will render these as clickable buttons.
```

- [ ] **Step 3: Delete removed files**

```bash
rm src/Agents/Orchestration/IThreadUI.cs
rm src/Agents/Orchestration/OptionsFallbackDetector.cs
rm test/Core.Tests/ThreadOptionsTests.cs
rm test/Core.Tests/OptionsFallbackTests.cs
```

- [ ] **Step 4: Build and test**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded

Run: `dotnet test test/Core.Tests -v m`
Expected: All tests pass (removed tests no longer run, remaining tests unaffected)

- [ ] **Step 5: Commit**

```bash
git add -u
git commit -m "refactor: remove PresentOptions, IThreadUI, and OptionsFallbackDetector"
```

---

### Task 6: Integration Verification

**Files:** None (verification only)

- [ ] **Step 1: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All tests pass

- [ ] **Step 2: Restart Aspire services**

Restart assistant and telegram resources. Check console logs for errors.

- [ ] **Step 3: Test via Telegram**

Send "Give me 3 jokes and let me pick the best" to the Telegram bot. Verify:
- Response has MarkdownV2 formatting (bold, italic, etc.)
- Inline buttons appear for joke selection
- Suggestion buttons appear for "another round" or similar
- Clicking an option button confirms selection
- Clicking a suggestion button sends it as a new message and continues conversation
- No error spans in traces

- [ ] **Step 4: Test fallback**

If the TelegramUIAgent fails (e.g., API quota exhausted), verify the response still appears as plain text without buttons.

- [ ] **Step 5: Final commit if any fixups needed**

```bash
git add -A
git commit -m "fix: integration fixups for TelegramUIAgent rich rendering"
```
