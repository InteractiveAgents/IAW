# Thread Delegation: Making ThreadAgent a Real Assistant

## Problem

The ThreadAgent (`IThread`) is the user-facing conversational grain — every Telegram topic, MCP chat, and DevUI conversation routes through it. But today it's a bare LLM wrapper with zero tools and generic instructions. When a user asks "what's the system status?" the Thread responds "I don't have access to that" — because it literally can't do anything beyond chat.

The entire agent system is running: AgentRegistry discovers agents at startup, AgentSelector picks the right team for a task, CodeOrchestrator generates and executes C# orchestration code. But Thread has no bridge to any of it.

## Design

### Unified Flow: All Clients Through Thread

Every conversational interaction goes through Thread. Different clients, same flow:

```
Telegram topic  --> IThread("470867712/iaw")   --+
MCP assistant   --> IThread("mcp/general")     --+--> Context enrichment
DevUI new chat  --> IThread("devui/{guid}")    --+    --> LLM + Delegate tool
                                                      --> Response
```

Direct agent access (MCP `agent_send_message`, DevUI agent picker) remains as a separate developer/admin flow — no Thread, no context enrichment, raw agent interaction.

### Thread ID Scoping

| Client   | Pattern          | Example              |
|----------|------------------|----------------------|
| Telegram | `{userId}/{slug}` | `470867712/iaw`     |
| MCP      | `mcp/{slug}`      | `mcp/general`       |
| DevUI    | `devui/{guid}`    | `devui/a3f8b2c1`    |

Shared context (user profile, memory agents) works across threads via the UserContextProvider extracting userId from the grain ID. Per-thread context (RAG, conversation history) is isolated by thread ID.

### One Tool: Delegate

ThreadAgent gets a single tool via `DefineAdditionalTools()` override. The IThread interface stays clean.

**`Delegate(string request)`** — delegates work to the agent system.

Internal flow:

```
Delegate(request)
  |
  +-- 1. AgentSelector.SelectAsync(request)
  |      Phase 1: Registry.SearchAsync() -> candidates
  |      Phase 2: LLM picks team + generates plan
  |      Returns SelectionResult { Status, SelectedAgents, Plan, Questions }
  |
  +-- 2. Branch on Status:
  |      NeedsClarification -> return Questions to user
  |      CannotHandle -> return explanation
  |      Ready -> continue
  |
  +-- 3. Branch on agent count:
  |      Single agent -> GrainFactory.Get<T>(threadId).GetResponse(request)
  |      Multiple agents -> CodeOrchestrator.ExecuteCodeOrchestration(plan)
  |
  +-- 4. Return result string to Thread LLM
         Thread summarizes/formats for user
```

**Grain scoping for delegated agents:**

- **AgentSelector** — use `GrainFactory.Get<IAgentSelector>()` (parameterless, unique ID each time). Selection is stateless — no history should accumulate. Each Delegate call gets a fresh ephemeral selector.
- **CodeOrchestrator** — use `GrainFactory.Get<ICodeOrchestrator>(threadId)` scoped to the thread. Orchestration may need multi-turn context within a task.
- **Single-agent dispatch** — scoped to thread: `GrainFactory.GetGrain(resolvedType, $"{threadId}/{interfaceName}")`. This matches the `Get<T>(scope)` key format (`{scope}/{typeof(T).Name}`).

**Single-agent resolution:** AgentSelector returns interface names like `"IGit"`. Resolve to grain reference at runtime:

1. Scan `AppDomain.CurrentDomain.GetAssemblies()` for interface types implementing `IAgent`
2. Match by name (e.g., `"IGit"` matches `typeof(IGit)`)
3. Call `GrainFactory.GetGrain(matchedType, $"{threadId}/{interfaceName}")` — key follows the same `{scope}/{Name}` pattern as `GrainFactoryExtensions.Get<T>(scope)`

This reflection pattern is already used in `AgentTools.cs` (MCP) and `OrleansAgentChatClient.cs` (DevUI). Inside the silo, all agent assemblies are loaded since the assistant project references all agent projects. A shared helper method should be extracted to avoid duplicating the resolution logic across ThreadAgent, MCP, and DevUI.

### Thread Instructions

Replace the generic "You are a helpful assistant" with instructions that tell the LLM what it is and when to delegate:

```
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

When delegating, describe WHAT needs to be done, not HOW. The agent
system handles routing and execution automatically.

Be concise and direct. Use markdown formatting.
```

### What Changes Where

| File | Change |
|------|--------|
| `src/Agents/Orchestration/ThreadAgent.cs` | Add `DefineAdditionalTools()` override with Delegate tool |
| `src/Agents/Orchestration/IThread.cs` | Update `AgentInstructions` static property |
| `src/IAW.MCP/Tools/AgentTools.cs` | Prefix thread slugs with `mcp/` in `assistant_chat` and `agent_assign_task`. This is a dev-only change — no production state migration needed. |
| `src/DevUI/OrleansAgentChatClient.cs` | Generate `devui/{guid}` IDs when targeting Thread |
| `src/Core/Extensions/AgentInterfaceResolver.cs` | Extract shared agent interface resolution logic (used by ThreadAgent, MCP, DevUI) |

### What Does NOT Change

- IThread interface methods (no new public contract)
- Agent base class (no core changes)
- AgentSelector (used as-is)
- CodeOrchestrator (used as-is)
- AgentRegistry (used as-is)
- Telegram bot service (already correct)
- Context providers (already working)

## Examples

### Simple delegation (single agent)
```
User: "what's the git status of the repo?"
Thread LLM -> Delegate("check git status of the current repository")
  -> AgentSelector picks IGit (single agent)
  -> GrainFactory.Get<IGit>(threadId).GetResponse("check status")
  -> Returns: "On branch main, 3 files modified..."
Thread LLM -> formats and returns to user
```

### Complex orchestration (multiple agents)
```
User: "review the latest commit and run tests"
Thread LLM -> Delegate("review the latest git commit and run dotnet tests")
  -> AgentSelector picks [IGit, IDotNet], generates plan
  -> CodeOrchestrator.ExecuteCodeOrchestration(plan)
  -> Generates C# that calls IGit for diff, IDotNet for tests
  -> Runs out-of-process, returns result.json
Thread LLM -> summarizes findings for user
```

### Direct answer (no delegation)
```
User: "hi, what can you do?"
Thread LLM -> answers from instructions (knows about agent system)
  -> "I'm your IAW assistant. I can delegate tasks to specialized agents..."
```

### Clarification needed
```
User: "fix it"
Thread LLM -> Delegate("fix it")
  -> AgentSelector returns NeedsClarification with questions
Thread LLM -> "Could you clarify what needs fixing? Are you referring to..."
```
