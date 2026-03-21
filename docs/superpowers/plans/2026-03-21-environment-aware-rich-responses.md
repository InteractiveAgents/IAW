# Environment-Aware Rich Responses Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable agents to present interactive options (buttons) during normal conversation responses, with a smart fallback for when the LLM forgets to use the tool.

**Architecture:** Thread agent gets a `PresentOptions` AI tool that stores options transiently on the grain. After streaming completes, the Telegram client consumes pending options via a separate `IThreadUI` interface and attaches inline keyboard buttons to the same message. A fallback heuristic auto-detects option patterns in plain text.

**Tech Stack:** Orleans grains, Microsoft.Extensions.AI, Telegram BotAPI inline keyboards, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-21-environment-aware-rich-responses-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Core/Contracts/UI/PendingOptions.cs` | Create | `PendingOptions` and `PendingOption` serializable records |
| `src/Agents/Orchestration/IThreadUI.cs` | Create | `IThreadUI` grain interface with `ConsumePendingOptions` |
| `src/Agents/Orchestration/IThread.cs` | Modify:15-33 | Add PresentOptions mention to system instructions |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify:18,46-52 | Implement `IThreadUI`, add `PresentOptionsAsync` tool, `_pendingOptions` field, `ConsumePendingOptions()` |
| `src/Core/Contracts/IUISession.cs` | Modify:9 | Add `RegisterOptions()` method signature |
| `src/Core/Contracts/UISessionDurableState.cs` | Modify:6-20 | Add `PendingOptionSets` dictionary |
| `src/Core/AI/UISessionStateMapper.cs` | Modify:22-28 | Wire new `PendingOptionSets` keyed service |
| `src/Agents/UI/UISession.cs` | Modify:56-131 | Add `RegisterOptions()`, add `"opt"` branch in `HandleCallback` |
| `src/Clients.Telegram/TelegramBotService.cs` | Modify:532-572 | Post-stream button attachment + smart fallback + selection-as-message |
| `src/Agents/Orchestration/OptionsFallbackDetector.cs` | Create | Pure static heuristic for detecting numbered-list options in text |
| `test/Core.Tests/UI/OptionsTests.cs` | Create | Tests for UISession options registration and callback |
| `test/Core.Tests/ThreadOptionsTests.cs` | Create | Tests for PresentOptions tool and ConsumePendingOptions |
| `test/Core.Tests/OptionsFallbackTests.cs` | Create | Tests for smart fallback heuristic |

---

### Task 1: PendingOptions Data Types

**Files:**
- Create: `src/Core/Contracts/UI/PendingOptions.cs`

- [ ] **Step 1: Create PendingOptions records**

```csharp
namespace Core.Contracts.UI;

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

- [ ] **Step 2: Build to verify serializer generation**

Run: `dotnet build src/Core/IAW.Core.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Core/Contracts/UI/PendingOptions.cs
git commit -m "feat: add PendingOptions and PendingOption records"
```

---

### Task 2: IThreadUI Interface

**Files:**
- Create: `src/Agents/Orchestration/IThreadUI.cs`

- [ ] **Step 1: Create IThreadUI interface**

This is a separate grain interface (NOT extending `IAgent`) so that `ConsumePendingOptions` is not auto-registered as an AI tool. `ThreadAgent` will implement both `IThread` and `IThreadUI`, sharing the same grain identity.

```csharp
using Core.Contracts.UI;

namespace IAW.Agents.Orchestration;

public interface IThreadUI : IGrainWithStringKey
{
    Task<PendingOptions?> ConsumePendingOptions(CancellationToken ct);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents/IAW.Agents.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/IThreadUI.cs
git commit -m "feat: add IThreadUI interface for consuming pending options"
```

---

### Task 3: UISession Options Registration and Callback

**Files:**
- Modify: `src/Core/Contracts/IUISession.cs:9`
- Modify: `src/Core/Contracts/UISessionDurableState.cs:6-20`
- Modify: `src/Core/AI/UISessionStateMapper.cs:22-28`
- Modify: `src/Agents/UI/UISession.cs:56-131`
- Create: `test/Core.Tests/UI/OptionsTests.cs`

- [ ] **Step 1: Write failing tests for options registration and callback**

Create `test/Core.Tests/UI/OptionsTests.cs`:

```csharp
using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class OptionsTestSiloConfigurator : ISiloConfigurator
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

public class OptionsTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<OptionsTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    [Fact]
    public async Task RegisterOptions_And_HandleCallback_ResolvesSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("opt-user-1");

        var options = new[]
        {
            new PendingOption("Joke A", "a"),
            new PendingOption("Joke B", "b"),
            new PendingOption("Joke C", "c")
        };
        await session.RegisterOptions("opt-abc123", "Which joke?", options, "proj/slug", ct);

        var result = await session.HandleCallback("opt-abc123", "opt:opt-abc123:b", ct);

        Assert.Contains("Joke B", result.NewText);
        Assert.Equal("b", result.Action);
        Assert.Null(result.Buttons);
    }

    [Fact]
    public async Task HandleCallback_UnknownOptionsId_ReturnsUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("opt-user-2");

        var result = await session.HandleCallback("x", "opt:nonexistent:a", ct);

        Assert.Equal("Unknown callback", result.Toast);
    }

    [Fact]
    public async Task RegisterOptions_SecondCall_OverwritesPrevious()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("opt-user-3");

        var first = new[] { new PendingOption("A", "a") };
        var second = new[] { new PendingOption("X", "x"), new PendingOption("Y", "y") };

        await session.RegisterOptions("opt-1", "First?", first, "proj", ct);
        await session.RegisterOptions("opt-1", "Second?", second, "proj", ct);

        var result = await session.HandleCallback("opt-1", "opt:opt-1:y", ct);
        Assert.Contains("Y", result.NewText);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OptionsTests" -v m`
Expected: FAIL -- `RegisterOptions` does not exist on `IUISession`

- [ ] **Step 3: Add RegisterOptions to IUISession**

In `src/Core/Contracts/IUISession.cs`, add after `ResolveApproval` (after line 9):

```csharp
Task RegisterOptions(string optionsId, string prompt, PendingOption[] options, string projectSlug, CancellationToken ct);
```

- [ ] **Step 4: Add PendingOptionSet to UISessionDurableState**

In `src/Core/Contracts/UISessionDurableState.cs`, add a new constructor parameter and property for `IDurableDictionary<string, PendingOptionSet>` where `PendingOptionSet` is a simple record to hold the registered options.

First, add the record to `src/Core/Contracts/UI/PendingOptions.cs` (append):

```csharp
[GenerateSerializer]
public sealed record PendingOptionSet(
    [property: Id(0)] string Id,
    [property: Id(1)] string Prompt,
    [property: Id(2)] IReadOnlyList<PendingOption> Options,
    [property: Id(3)] string ProjectSlug,
    [property: Id(4)] DateTimeOffset CreatedAt);
```

Then update `UISessionDurableState.cs` to add the new parameter:
```csharp
public sealed class UISessionDurableState(
    IDurableDictionary<string, PendingApproval> pendingApprovals,
    IDurableDictionary<string, WizardState> wizards,
    IDurableDictionary<string, string> pendingFreeText,
    IDurableDictionary<string, PaginatorState> paginators,
    IDurableDictionary<string, MenuState> menus,
    IDurableDictionary<string, FormState> forms,
    IDurableDictionary<string, PendingOptionSet> pendingOptionSets)
{
    // ... existing properties ...
    public IDurableDictionary<string, PendingOptionSet> PendingOptionSets => pendingOptionSets;
}
```

- [ ] **Step 5: Wire PendingOptionSets in UISessionStateMapper**

In `src/Core/AI/UISessionStateMapper.cs`, add the new keyed service resolution after the forms line:

```csharp
services.GetRequiredKeyedService<IDurableDictionary<string, PendingOptionSet>>("ui-pending-option-sets"));
```

Add `using Core.Contracts.UI;` if not already present.

- [ ] **Step 6: Implement RegisterOptions and opt branch in UISession**

In `src/Agents/UI/UISession.cs`:

Add `RegisterOptions` method (after `RegisterApproval`):
```csharp
public Task RegisterOptions(string optionsId, string prompt, PendingOption[] options, string projectSlug, CancellationToken ct)
{
    state.PendingOptionSets[optionsId] = new PendingOptionSet(
        optionsId, prompt, options, projectSlug, DateTimeOffset.UtcNow);
    return Task.CompletedTask;
}
```

Add `"opt"` branch in `HandleCallback` method, after the `"fm"` block (before the final `return`):
```csharp
if (type == "opt" && state.PendingOptionSets.TryGetValue(id, out var optionSet))
{
    var selectedOption = optionSet.Options.FirstOrDefault(o => o.Value == action);
    var label = selectedOption?.Label ?? action;
    state.PendingOptionSets.Remove(id);
    return new CallbackResult(
        $"\u2705 {optionSet.Prompt} \u2014 {label}", action, null);
}
```

Add `PendingOptionSets` cleanup in the `ReceiveReminder` method (after the forms cleanup block):
```csharp
foreach (var key in state.PendingOptionSets.Keys.ToList())
    if (now - state.PendingOptionSets[key].CreatedAt > WizardFormTimeout)
        state.PendingOptionSets.Remove(key);
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OptionsTests" -v m`
Expected: All 3 tests PASS

- [ ] **Step 8: Commit**

```bash
git add src/Core/Contracts/IUISession.cs src/Core/Contracts/UISessionDurableState.cs src/Core/AI/UISessionStateMapper.cs src/Core/Contracts/UI/PendingOptions.cs src/Agents/UI/UISession.cs test/Core.Tests/UI/OptionsTests.cs
git commit -m "feat: add options registration and callback to UISession"
```

---

### Task 4: ThreadAgent PresentOptions Tool and IThreadUI

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs:18,46-52`
- Modify: `src/Agents/Orchestration/IThread.cs:15-33`
- Create: `test/Core.Tests/ThreadOptionsTests.cs`

- [ ] **Step 1: Write failing tests**

Create `test/Core.Tests/ThreadOptionsTests.cs`. Tests verify `ConsumePendingOptions` behavior via `IThreadUI`. Note: `PresentOptionsAsync` is a private method invoked by the LLM tool system -- we test its effects indirectly. The grain exposes `IThreadUI` which we can call directly. For the "stores and consumes" flow, we test by calling the grain's `ConsumePendingOptions` after manually setting state (or verifying null when nothing is set):

```csharp
using Core.Contracts;
using Core.Contracts.UI;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ThreadOptionsTests : AgentTest<ThreadAgent>
{
    [Fact]
    public async Task ConsumePendingOptions_ReturnsNullWhenNoPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var threadUI = Cluster.Client.GetGrain<IThreadUI>(UniqueId("no-opts"));

        var result = await threadUI.ConsumePendingOptions(ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumePendingOptions_IsOneShot_SecondCallReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("oneshot");
        var threadUI = Cluster.Client.GetGrain<IThreadUI>(id);

        // first call: null (nothing pending)
        var first = await threadUI.ConsumePendingOptions(ct);
        Assert.Null(first);

        // second call: still null (idempotent on empty)
        var second = await threadUI.ConsumePendingOptions(ct);
        Assert.Null(second);
    }

    [Fact]
    public async Task DefineAdditionalTools_IncludesPresentOptions()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("tools");
        var thread = Agent(id);

        // GetCapabilities returns the registered tool names
        var caps = await thread.GetCapabilities(ct);
        Assert.Contains("PresentOptions", caps.Tools);
        Assert.Contains("Delegate", caps.Tools);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ThreadOptionsTests" -v m`
Expected: FAIL -- `IThreadUI` not implemented by `ThreadAgent`

- [ ] **Step 3: Implement IThreadUI on ThreadAgent**

In `src/Agents/Orchestration/ThreadAgent.cs`:

1. Add `IThreadUI` to the class declaration (line 18):
```csharp
    : Agent<IThread>(durableState, chatClient), IThread, IThreadUI
```

2. Add the transient field (after `_contextProviders` on line 22):
```csharp
    private PendingOptions? _pendingOptions;
```

3. Add `ConsumePendingOptions` method (at the end of the class, before the closing brace):
```csharp
    public Task<PendingOptions?> ConsumePendingOptions(CancellationToken ct)
    {
        var result = _pendingOptions;
        _pendingOptions = null;
        return Task.FromResult(result);
    }
```

4. Add `PresentOptionsAsync` tool method (after `DelegateAsync`):
```csharp
    private async Task<string> PresentOptionsAsync(string prompt, string[] options, CancellationToken ct = default)
    {
        var callbackId = $"opt-{Guid.NewGuid().ToString("N")[..8]}";
        var pendingOptions = options.Select(o => new PendingOption(o, o)).ToArray();

        _pendingOptions = new PendingOptions(
            callbackId, prompt, pendingOptions, DateTimeOffset.UtcNow.AddMinutes(30));

        var threadId = this.GetPrimaryKeyString();
        var userId = threadId.Contains('/') ? threadId.Split('/')[0] : threadId;
        var session = GrainFactory.GetGrain<IUISession>(userId);
        await session.RegisterOptions(callbackId, prompt, pendingOptions, threadId, ct);

        return "Options presented to user. Waiting for selection.";
    }
```

5. Register the tool in `DefineAdditionalTools()` (line 46-52), add to the returned array:
```csharp
    protected override IReadOnlyList<AITool> DefineAdditionalTools()
    {
        return [
            AIFunctionFactory.Create(DelegateAsync, "Delegate",
                "Delegate a task to the IAW agent system. Use this for any request that requires " +
                "code execution, system operations, builds, git, file operations, or specialized agent skills. " +
                "Describe WHAT needs to be done."),
            AIFunctionFactory.Create(PresentOptionsAsync, "PresentOptions",
                "Present interactive options to the user as clickable buttons. " +
                "Use when offering choices, comparisons, votes, or any pick-one scenario.")
        ];
    }
```

6. Add `using Core.Contracts.UI;` to the top of the file.

- [ ] **Step 4: Update IThread instructions**

In `src/Agents/Orchestration/IThread.cs`, update `AgentInstructions` to mention PresentOptions (replace lines 15-33):

```csharp
    static string IAgent.AgentInstructions => """
        You are an AI assistant in the IAW (Interactive Agents Workspace) system —
        a multi-agent platform built on Orleans. You have access to a team of
        specialized agents that can execute tasks: coding, git, shell, .NET builds,
        code review, and more.

        DECISION RULE:
        - Answer directly when: greetings, general knowledge, questions about
          conversation context, user preferences, or anything you can answer
          from your enriched context
        - Use the Delegate tool when: the request involves code execution,
          system operations, agent capabilities, builds, git, file operations,
          or anything requiring specialized agent skills
        - Use the PresentOptions tool when: offering choices, comparisons,
          votes, polls, or any scenario where the user should pick from a list.
          The user's environment will render these as clickable buttons.

        When delegating, describe WHAT needs to be done, not HOW. The agent
        system handles routing and execution automatically.

        Be concise and direct. Use markdown formatting.
        """;
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ThreadOptionsTests" -v m`
Expected: PASS

- [ ] **Step 6: Build the full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs src/Agents/Orchestration/IThread.cs src/Agents/Orchestration/IThreadUI.cs test/Core.Tests/ThreadOptionsTests.cs
git commit -m "feat: add PresentOptions AI tool and IThreadUI to ThreadAgent"
```

---

### Task 5: Smart Fallback Heuristic

**Files:**
- Create: `src/Agents/Orchestration/OptionsFallbackDetector.cs`
- Create: `test/Core.Tests/OptionsFallbackTests.cs`

Note: `OptionsFallbackDetector` lives in `src/Agents` (not `src/Clients.Telegram`) because it is a pure static utility with no Telegram dependency, and `test/Core.Tests` already references `Agents.csproj`.

- [ ] **Step 1: Write failing tests for the fallback heuristic**

Create `test/Core.Tests/OptionsFallbackTests.cs`. This is a pure logic class, no Orleans needed:

```csharp
using IAW.Agents.Orchestration;
using Xunit;

namespace IAW.Core.Tests;

public class OptionsFallbackTests
{
    [Fact]
    public void DetectsNumberedListWithQuestion()
    {
        var text = """
            Here are 3 jokes:

            1. The Developer
            2. The DBA
            3. The PM

            Which one is the best?
            """;

        var result = OptionsFallbackDetector.TryDetect(text);

        Assert.NotNull(result);
        Assert.Equal(3, result.Value.Labels.Count);
        Assert.Equal("The Developer", result.Value.Labels[0]);
        Assert.Equal("The DBA", result.Value.Labels[1]);
        Assert.Equal("The PM", result.Value.Labels[2]);
    }

    [Fact]
    public void DetectsSelectTriggerWord()
    {
        // preamble ensures the numbered list starts past the 30% threshold
        var preamble = "Here is some context about colors and their meanings. " +
                       "Let me explain the differences between these options for you.\n\n";
        var text = $"{preamble}1. Red\n2. Blue\n\nPlease select one.";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.NotNull(result);
        Assert.Equal(2, result.Value.Labels.Count);
    }

    [Fact]
    public void IgnoresListInMiddleOfText()
    {
        var longPreamble = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"Line {i} of explanation."));
        var text = $"""
            {longPreamble}

            1. First item
            2. Second item

            Which do you pick?

            But actually there is more to consider. Let me explain further.
            This is a long conclusion paragraph that makes the list appear
            in the first half of the text, not the last 30%.
            More text here to push the ratio.
            Even more text.
            And more.
            """;
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresMoreThan8Items()
    {
        var items = string.Join("\n", Enumerable.Range(1, 9).Select(i => $"{i}. Item {i}"));
        var text = $"{items}\n\nWhich one?";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresSingleItem()
    {
        var text = "1. Only one\n\nPick one?";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresLongLabels()
    {
        var longLabel = new string('A', 65);
        var text = $"1. Short\n2. {longLabel}\n\nChoose?";
        var result = OptionsFallbackDetector.TryDetect(text);
        // Should detect but skip the long label, leaving only 1 item -> below minimum
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresMultiLineParagraphs()
    {
        var text = """
            1. This is a long paragraph
            that spans multiple lines
            2. This is another paragraph
            with extra detail

            Which one?
            """;
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void NoNumberedList_ReturnsNull()
    {
        var text = "Just a plain response with no options.";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OptionsFallbackTests" -v m`
Expected: FAIL -- `OptionsFallbackDetector` does not exist

- [ ] **Step 3: Implement OptionsFallbackDetector**

Create `src/Agents/Orchestration/OptionsFallbackDetector.cs`:

```csharp
using System.Text.RegularExpressions;

namespace IAW.Agents.Orchestration;

public static partial class OptionsFallbackDetector
{
    public readonly record struct DetectedOptions(IReadOnlyList<string> Labels);

    const int MinItems = 2;
    const int MaxItems = 8;
    const int MaxLabelLength = 64;

    static readonly string[] TriggerWords = ["choose", "select", "pick", "vote", "which", "?"];

    [GeneratedRegex(@"^\d+\.\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex NumberedItemRegex();

    public static DetectedOptions? TryDetect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var matches = NumberedItemRegex().Matches(text);
        if (matches.Count < MinItems || matches.Count > MaxItems)
            return null;

        var firstMatchPos = matches[0].Index;
        var textLength = text.Length;

        // only trigger when list starts in the last ~70% of the response
        if (firstMatchPos < textLength * 0.3)
            return null;

        // check for trigger words after the last match
        var lastMatch = matches[^1];
        var afterList = text[(lastMatch.Index + lastMatch.Length)..];
        var hasTrigger = TriggerWords.Any(t =>
            afterList.Contains(t, StringComparison.OrdinalIgnoreCase));

        if (!hasTrigger)
            return null;

        var labels = new List<string>();
        foreach (Match match in matches)
        {
            var label = match.Groups[1].Value.Trim();

            // skip multi-line items: check if text between this match and the next
            // contains non-match lines
            var matchEnd = match.Index + match.Length;
            var nextMatchStart = matches.Count > labels.Count + 1
                ? matches[labels.Count + 1].Index
                : text.Length;
            var between = text[matchEnd..nextMatchStart];
            if (between.Trim().Length > 0 && between.Contains('\n') &&
                between.Split('\n').Any(l => l.Trim().Length > 0 && !NumberedItemRegex().IsMatch(l.Trim())))
            {
                return null; // multi-line paragraph detected
            }

            if (label.Length > MaxLabelLength)
                continue;

            labels.Add(label);
        }

        if (labels.Count < MinItems)
            return null;

        return new DetectedOptions(labels);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OptionsFallbackTests" -v m`
Expected: All 8 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/OptionsFallbackDetector.cs test/Core.Tests/OptionsFallbackTests.cs
git commit -m "feat: add OptionsFallbackDetector for smart option detection in text"
```

---

### Task 6: Telegram Client Integration

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs:1-9,532-572,132-172`

This task wires everything together in the Telegram client. Three changes:

1. Post-stream button attachment in `StreamResponseAsync`
2. Selection-as-new-message after button click in `HandleCallbackQueryAsync`
3. Uses to `IThreadUI` and `OptionsFallbackDetector`

- [ ] **Step 1: Add IThreadUI using and IAW.Agents.Orchestration import**

At the top of `TelegramBotService.cs`, ensure these are present:
- `using IAW.Agents.Orchestration;` (already on line 7)
- `using Core.Contracts.UI;` (add if missing)

- [ ] **Step 2: Modify StreamResponseAsync for post-stream button attachment**

Replace lines 532-572 of `TelegramBotService.cs` with the enhanced version. After the streaming loop and final `EditSafe`, add logic to consume pending options and attach buttons:

```csharp
    private async Task StreamResponseAsync(
        long chatId, int messageId, int? topicId, IThread thread, ChatMessage chatMessage, long telegramId, CancellationToken ct)
    {
        const int maxChars = 4000;
        var buffer = new StringBuilder();
        var currentMessageId = messageId;
        var lastEditAt = DateTimeOffset.MinValue;

        try
        {
            await foreach (var chunk in thread.GetResponseStream(chatMessage, ct))
            {
                buffer.Append(chunk);

                if (buffer.Length > maxChars)
                {
                    await EditSafe(chatId, currentMessageId, buffer.ToString());

                    var continuation = await botClient.SendMessageAsync(chatId, "...", messageThreadId: topicId);
                    currentMessageId = continuation.MessageId;
                    buffer.Clear();
                    lastEditAt = DateTimeOffset.MinValue;
                    continue;
                }

                if ((DateTimeOffset.UtcNow - lastEditAt).TotalMilliseconds > 500)
                {
                    await EditSafe(chatId, currentMessageId, buffer.ToString());
                    lastEditAt = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming response from thread for user {TelegramId}", telegramId);
            buffer.Append("\n\n[Error communicating with assistant]");
        }

        var finalText = buffer.ToString();

        // try to attach inline buttons from PresentOptions tool call
        var threadId = thread.GetPrimaryKeyString();
        var threadUI = clusterClient.GetGrain<IThreadUI>(threadId);
        var pending = await threadUI.ConsumePendingOptions(ct);

        if (pending is not null)
        {
            await AttachOptionsButtons(chatId, currentMessageId, finalText, pending);
        }
        else
        {
            // smart fallback: detect numbered list options in text
            var detected = OptionsFallbackDetector.TryDetect(finalText);
            if (detected is not null)
            {
                var callbackId = $"opt-{Guid.NewGuid().ToString("N")[..8]}";
                var pendingOptions = detected.Value.Labels
                    .Select(l => new PendingOption(l, l)).ToArray();
                var userId = telegramId.ToString();
                var session = clusterClient.GetGrain<IUISession>(userId);
                await session.RegisterOptions(callbackId, "", pendingOptions, threadId, ct);

                var fallbackPending = new PendingOptions(callbackId, "", pendingOptions,
                    DateTimeOffset.UtcNow.AddMinutes(30));
                await AttachOptionsButtons(chatId, currentMessageId, finalText, fallbackPending);
            }
            else if (finalText.Length > 0)
            {
                await EditSafe(chatId, currentMessageId, finalText);
            }
        }
    }

    private async Task AttachOptionsButtons(long chatId, int messageId, string text, PendingOptions pending)
    {
        var buttons = pending.Options.Select(o =>
            new InlineKeyboardButton(o.Label) { CallbackData = $"opt:{pending.CallbackId}:{o.Value}" }
        ).ToArray();
        var keyboard = new InlineKeyboardMarkup([buttons]);

        var displayText = !string.IsNullOrEmpty(pending.Prompt) && !text.Contains(pending.Prompt)
            ? $"{text}\n\n{pending.Prompt}"
            : text;

        if (string.IsNullOrWhiteSpace(displayText))
            displayText = pending.Prompt;

        try
        {
            await botClient.EditMessageTextAsync(chatId, messageId, displayText, replyMarkup: keyboard);
        }
        catch (BotRequestException)
        {
            try
            {
                await botClient.EditMessageTextAsync(chatId, messageId, displayText,
                    replyMarkup: keyboard, parseMode: null);
            }
            catch (BotRequestException ex) when (
                ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
            {
            }
        }
    }
```

- [ ] **Step 3: Add selection-as-new-message in HandleCallbackQueryAsync**

In `HandleCallbackQueryAsync`, after the existing block that edits the message with `result.NewText` (around line 156-171), add logic to detect `opt:` results and feed the selection back as a new message.

After the `if (result.NewText is not null ...)` block, before the method's closing brace, add:

```csharp
        // for options callbacks, feed selection back into conversation
        if (callbackQuery.Data?.StartsWith("opt:") == true && result.Action is not null)
        {
            var optParts = callbackQuery.Data.Split(':', 3);
            if (optParts.Length >= 3)
            {
                var from = callbackQuery.From;
                var topicId = callbackQuery.Message?.MessageThreadId;
                var (thread, _) = await ResolveThreadAsync(from.Id, topicId, ct);

                var selectedLabel = result.NewText?.Contains('\u2014') == true
                    ? result.NewText.Split('\u2014', 2).Last().Trim()
                    : result.Action;
                // extract original prompt from NewText (format: "checkmark prompt -- label")
                var originalPrompt = result.NewText?.Contains('\u2014') == true
                    ? result.NewText.Split('\u2014', 2).First().Replace("\u2705", "").Trim()
                    : "";
                var contextPrefix = !string.IsNullOrEmpty(originalPrompt)
                    ? $"Re: '{originalPrompt}' -- " : "";
                var selectionMessage = BuildChatMessage($"{contextPrefix}I choose: {selectedLabel}");

                var sent = await botClient.SendMessageAsync(chatId, "...", messageThreadId: topicId);
                await StreamResponseAsync(chatId, sent.MessageId, topicId, thread, selectionMessage, from.Id, ct);
            }
        }
```

- [ ] **Step 4: Build the full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: wire post-stream buttons and selection feedback in Telegram client"
```

---

### Task 7: Full Build and Integration Verification

**Files:** None (verification only)

- [ ] **Step 1: Run all unit tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All tests pass

- [ ] **Step 2: Start Aspire and verify via MCP**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`

Use Aspire MCP tools to verify the system starts cleanly:
- Check `list_resources` for all services healthy
- Check `list_console_logs` for telegram resource -- no errors

- [ ] **Step 3: Send test message via Telegram**

Send "Give me 3 options and let me pick one" to the Telegram bot. Verify:
- Response appears with inline keyboard buttons
- Clicking a button removes buttons and shows confirmation
- Assistant continues the conversation with the selection context

- [ ] **Step 4: Final commit if any fixups needed**

```bash
git add -A
git commit -m "fix: integration fixups for environment-aware rich responses"
```
