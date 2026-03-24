# IAW Agent Team v2: Redesign, Self-Improving Closed Loop & Prompt Engineering

## Overview

Ground-up redesign of the IAW agent team with three goals:
1. **Right agent for the right job** — each agent owns its domain completely with proper tooling
2. **Self-improving closed loop** — agents fix themselves on demand, test in sandbox, hot-deploy via Aspire restart
3. **Best-in-class prompt engineering** — three-layer instructions, actionable errors, negative examples, minimal token footprint

## Design Principles

- **Agents are autonomous specialists.** Thread routes, agents execute. No agent does another agent's job.
- **Tool descriptions are first-class prompt artifacts.** Every `[Description]` attribute is engineered, not placeholder text.
- **Errors are actionable.** Every tool failure tells the LLM what to try instead.
- **Negative examples prevent misrouting.** "DO NOT use Orchestrate for single-agent tasks" > "prefer SendToAgent."
- **Full PC access.** No workspace restriction. Agents can operate anywhere on the machine.
- **The system heals itself.** User says "fix X" → agent reads traces/code → fixes → tests → deploys.

---

## Part 1: Agent Team Redesign

### Agent Responsibility Matrix

| Agent | Model | Domain | Owns | Does NOT Do |
|-------|-------|--------|------|-------------|
| **Thread** | Balanced (gpt-5.4-mini) | Routing & conversation | User interaction, routing to agents, answering questions | Any tool execution — always delegates |
| **Shell** | Haiku | Raw CLI execution | `cmd.exe`, `bash`, `npm`, `pip`, `cargo`, arbitrary scripts | .NET-specific operations (use DotNet) |
| **DotNet** | Sonnet 4.6 | .NET toolchain | Build, run, test, publish, new, format, NuGet. Auto-discovers .csproj/.sln | Raw shell commands (use Shell) |
| **Roslyn** | Sonnet 4.6 | C# code intelligence | Code analysis, type maps, error diagnostics, code generation/modification | Building or running projects (use DotNet) |
| **FileSystem** | Fast (gpt-5.4-nano) | File operations | Read, write, list, search across entire PC | Code analysis (use Roslyn), builds (use DotNet) |
| **Git** | Fast (gpt-5.4-nano) | Version control | Status, commit, diff, log, branch, merge, revert | File content modification (use FileSystem) |
| **Aspire** | Sonnet 4.6 | Infrastructure operations | Start/stop/restart resources, read traces, read logs, clean old logs, deploy code changes | Direct process management (use Shell) |
| **CodeOrchestrator** | Opus 4.6 | Complex multi-agent coordination | Generating C# apps that coordinate multiple agents | Simple single-agent tasks (Thread uses SendToAgent) |
| **AgentSelector** | Sonnet 4.6 | Team selection | Picking agent teams for orchestration | Simple routing (Thread handles via SendToAgent) |

### DotNet Agent — Full Rewrite

Current DotNet agent can't find `.csproj` files and its LLM doesn't call tools. The new DotNet agent:

**Tools (registered via interface + additional tools):**
- `Build(projectOrDirectory, configuration)` — auto-discovers .csproj/.sln from directory path
- `Run(projectOrDirectory, arguments)` — runs `dotnet run` with 120s timeout + process kill
- `Test(projectOrDirectory, filter)` — runs tests with optional filter, parses results
- `Publish(projectOrDirectory, configuration, output)` — publishes the project
- `New(template, name, outputDirectory)` — scaffolds new project from template
- `ListProjects(directory)` — finds all .csproj/.sln files in a directory tree
- `Format(projectOrDirectory)` — formats code with editorconfig

**Key change:** Every tool that takes a path auto-resolves directories to project files. No more "can't find solution" failures.

**Instructions (three-layer pattern):**
```
Layer 1 (Identity): You are DotNet, the .NET toolchain specialist. You build, run, test,
and publish .NET projects. Execute operations immediately and report results.

Layer 2 (Rules):
- ALWAYS call the appropriate tool. Never respond with instructions for the user to run manually.
- When given a directory path, use ListProjects first if unsure which project to target.
- For build errors, include the full diagnostic output.
- DO NOT execute raw shell commands. Use your typed tools.

Layer 3 (Reference): Available tools: Build, Run, Test, Publish, New, ListProjects, Format.
```

### Shell Agent — Scoped to Raw CLI Only

Shell keeps its current tools but instructions clarify scope:
```
You execute shell commands. For .NET operations (build, test, run), tell the user
to use the DotNet agent instead. You handle: npm, pip, cargo, scripts, system commands.

DO NOT run 'dotnet build' or 'dotnet run' — the DotNet agent handles .NET operations.
```

### Aspire Agent — Becomes the Deployment Operator

Current Aspire agent is a marker interface. The new Aspire agent:

**Tools:**
- `ListResources()` — list all Aspire resources and their state
- `RestartResource(resourceName)` — restart a specific resource
- `StopResource(resourceName)` — stop a resource
- `StartResource(resourceName)` — start a stopped resource
- `GetTraces(resourceName, count)` — get recent traces with token usage
- `GetLogs(resourceName, count)` — get structured logs
- `CleanLogs(resourceName, olderThanMinutes)` — clean old logs to prevent bloat
- `GetResourceHealth()` — health status of all resources

**Instructions:**
```
You are Aspire, the infrastructure and deployment operator. You manage the IAW
distributed system via the Aspire dashboard.

CAPABILITIES:
- Start, stop, restart any resource (assistant, telegram, devui, mcp, etc.)
- Read distributed traces with token usage metrics
- Read and clean structured logs
- Monitor resource health

RULES:
- When asked to "deploy" or "apply changes": rebuild the solution, then restart the assistant resource.
- Regularly clean logs older than 30 minutes to prevent accumulation.
- When restarting: stop resource, wait 3 seconds, start resource.
- Report resource state changes clearly.
```

**Implementation:** These tools call the Aspire MCP server internally (same protocol Claude Code uses), OR use the Aspire resource service gRPC API directly since the silo has access to the AppHost's service endpoint via environment variables.

### Thread Agent — Improved Routing

**Instructions update:**
```
ROUTING RULES:
- Answer directly: greetings, general knowledge, conversation context
- SendToAgent for single-agent tasks:
  • "DotNet" — build, run, test, publish, new project, format (.NET operations)
  • "Shell" — npm, pip, cargo, scripts, non-.NET CLI commands
  • "FileSystem" — read, write, list, search files anywhere on the PC
  • "Git" — status, commit, diff, log, branch, merge
  • "Roslyn" — analyze code, type maps, fix compilation errors, generate C# code
  • "Aspire" — start/stop/restart services, read traces, check health, deploy changes
  • "GitHub" — PRs, issues, repository operations
- Orchestrate ONLY for complex tasks needing 3+ agents coordinated together

DO NOT use Orchestrate for tasks that one agent can handle alone.
DO NOT route .NET build/run/test to Shell — always use DotNet.
For "fix yourself" or "improve" requests — see self-improvement flow below.
```

### Workspace Removal

Remove `.WithWorkspace("D:\\IAW-Workspace")` from `AppHost.cs`. The workspace concept becomes optional — agents default to `Directory.GetCurrentDirectory()` if no workspace is set, but can operate on any path the user provides. Full PC access by design (already established in PR #7).

---

## Part 2: Self-Improving Closed Loop

### The Loop

```
User: "fix voice transcription and re-send my voice message"
    ↓
Thread: understands this is a self-improvement request
    ↓
Thread: SendToAgent("Aspire", "get recent traces for voice transcription failures")
    ↓
Aspire agent: returns trace data showing where transcription failed
    ↓
Thread: SendToAgent("FileSystem", "read the transcription agent source code")
    ↓
Thread: SendToAgent("Roslyn", "analyze this code and the trace — what's the bug?")
    ↓
Roslyn: "The Whisper endpoint URL is wrong / the audio format isn't supported / etc."
    ↓
Thread: SendToAgent("FileSystem", "write the fix to the source file")
    ↓
Thread: SendToAgent("DotNet", "build the solution E:\IAW\IAW.slnx")
    ↓
DotNet: "Build succeeded, 0 errors"
    ↓
Thread: SendToAgent("DotNet", "run the tests")
    ↓
DotNet: "409 passed, 2 pre-existing failures"
    ↓
Thread: SendToAgent("Aspire", "restart the assistant resource to deploy changes")
    ↓
Aspire: restarts assistant → new code is live within 15-20s
    ↓
Thread: SendToAgent("DotNet", "re-send the voice message / re-run the failed task")
    ↓
Thread: "Fixed! Voice transcription now works. Here's the result: ..."
```

### What Makes This Work

1. **Agents can read their own source code** — FileSystem has full PC access to `E:\IAW\src\`
2. **Agents can read their own traces** — Aspire agent reads Aspire dashboard data
3. **Agents can modify their own code** — FileSystem writes to source files
4. **Agents can build and test** — DotNet builds the solution and runs tests
5. **Agents can deploy** — Aspire restarts resources after build succeeds
6. **Durable state survives restart** — Orleans journaled state persists across restarts
7. **The user stays in the same conversation** — Thread maintains context throughout

### Safety Rails

- **Build must succeed before deploy.** If `dotnet build` fails, the fix is wrong — agent iterates.
- **Tests must pass before deploy.** No deployment if test count drops.
- **Git branch per fix.** Changes go on a branch, committed with descriptive message.
- **Human can review.** Thread reports what it changed and why. User can reject.
- **Rollback capability.** `git revert` + Aspire restart returns to previous state.
- **No self-modification of safety rails.** The closed-loop logic itself is not modifiable by agents.

### Aspire Log Cleanup

The Aspire agent runs periodic cleanup as a scheduled job:
- Every 30 minutes, clean structured logs older than 1 hour
- Clean console logs older than 30 minutes
- This prevents the log accumulation issue that makes traces unreadable

---

## Part 3: Prompt Engineering Overhaul

### Three-Layer Instruction Pattern (Applied to Every Agent)

Every agent's `AgentInstructions` follows:

**Layer 1 — Identity & Constraints** (2-3 sentences)
- Who you are, what you specialize in
- What you cannot/should not do

**Layer 2 — Decision Rules** (concrete routing/behavior rules)
- When to use which tool
- Negative examples (DO NOT do X)
- Error handling behavior

**Layer 3 — Reference Data** (grounded context)
- Available tools with brief descriptions
- Valid inputs/outputs
- Known limitations

### Tool Description Engineering

Every `[Description]` attribute on interface methods follows this template:
```
[Description("{verb} {object}. {one sentence on when to use}. Returns {output shape}.")]
```

Example:
```csharp
[Description("Build a .NET project or solution. Pass a directory path or .csproj/.sln path. Returns success/failure with error count, warning count, and diagnostics.")]
Task<BuildRunResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default);
```

Bad (current):
```csharp
[Description("")]  // empty!
Task<string> StatusAsync(string repoPath, CancellationToken ct = default);
```

### Actionable Error Messages

Every tool failure returns: **what failed + why + what to try instead.**

```csharp
// Bad (current):
return $"Agent {agentName} failed: {ex.Message}";

// Good (new):
return $"Agent {agentName} failed: {ex.Message}. " +
       $"Try: {SuggestAlternative(agentName, ex)}";
```

### Compact Tool Results

Tool results that flow back to Thread should be summarized. Full output goes to durable state / traces.

```csharp
// Bad: return entire build output (2KB+)
return fullOutput;

// Good: return summary (100-200 bytes)
return $"Build {(success ? "succeeded" : "FAILED")}: {errors} errors, {warnings} warnings, {duration}ms";
```

---

## Part 4: Infrastructure Changes

### AppHost Changes

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Gpt54Mini>().AsBalanced()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Gpt54Nano>().AsFast()
    .WithLLM<Sonnet46>()
    .WithLLM<Opus46>().AsReasoning()
    .WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text<WhisperLargeV3Turbo>();
    // NO .WithWorkspace() — full PC access, agents resolve paths themselves
```

### Project Rename

`IAW.Assistant` → consider keeping as-is for now. Renaming is a separate effort that touches CI, Docker, Aspire references, and documentation. Not worth the risk in this change.

### Model Assignment Summary

| Agent | Model | Why |
|-------|-------|-----|
| Thread | Balanced (gpt-5.4-mini) | Routing decisions — moderate reasoning |
| Shell | Haiku | Pick which CLI command to run — simple |
| DotNet | Sonnet 4.6 | Needs to reason about project structure, errors |
| Roslyn | Sonnet 4.6 | Code analysis requires strong reasoning |
| FileSystem | Fast (gpt-5.4-nano) | Pick read/write/list — trivial routing |
| Git | Fast (gpt-5.4-nano) | Pick status/commit/diff — trivial routing |
| Aspire | Sonnet 4.6 | Infrastructure decisions need careful reasoning |
| CodeOrchestrator | Opus 4.6 | Code generation needs strongest model |
| AgentSelector | Sonnet 4.6 | Team selection needs good judgment |

---

## Implementation Phases

### Phase 1: Agent Team Redesign (foundation)
- Rewrite DotNet agent with full tooling (Build, Run, Test, Publish, New, ListProjects)
- Rewrite Aspire agent with resource management + trace reading + log cleanup
- Update Shell instructions to exclude .NET operations
- Update all agent `[Description]` attributes to engineering quality
- Update all agent instructions to three-layer pattern
- Remove `.WithWorkspace()` from AppHost
- Update Thread routing instructions

### Phase 2: Self-Improving Closed Loop
- Thread recognizes "fix yourself" / self-improvement requests
- FileSystem reads IAW source code
- Roslyn analyzes code + trace data to diagnose issues
- FileSystem writes fixes
- DotNet builds and tests
- Git commits changes on branch
- Aspire deploys via resource restart
- Aspire scheduled job for log cleanup (every 30 min)

### Phase 3: Optimization & Polish
- Compact tool results (summaries to LLM, full output to traces)
- Actionable error messages with suggestions
- Non-LLM pre-filters for deterministic error patterns
- Pattern caching for repeated orchestration requests
- Golden-path trace-based tests

---

## Success Criteria

1. **Simple tasks (build, run, read file, git status) complete in <10s with <5K tokens**
2. **No agent does another agent's job** — DotNet handles all .NET, Shell handles raw CLI
3. **"Fix yourself" works end-to-end** — user reports bug → agents diagnose, fix, test, deploy
4. **Aspire logs stay clean** — automatic cleanup prevents accumulation
5. **Every tool has an engineered `[Description]`** — no empty or placeholder descriptions
6. **Full PC access** — no workspace restriction, agents operate on any path
