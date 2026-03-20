# Enhanced Roslyn Intelligence: IDE-Grade Code Analysis & Modification

## Problem

The RoslynAgent today is a read-only syntax parser with a crippled semantic model. It creates compilations with only `typeof(object).Assembly` as a reference — meaning it can't resolve NuGet types, project references, or even `System.Linq`. It has no code modification tools, no call graph, no inheritance tracing. When an agent tries to modify C# code, it falls back to text manipulation — leading to broken imports, wrong types, and missing context.

For IAW to self-modify (accept a feature request in Telegram and implement it), the coding agents need IDE-grade intelligence: full type resolution, structural code edits, semantic refactoring, and understanding of who-calls-what across the entire solution.

## Design

### 1. Solution-Level Workspace Loading

On grain activation, RoslynAgent:

1. Reads durable state for cached type map, call graph, and inheritance tree — available immediately for simple queries
2. Returns to caller — grain is "warm" with cached data
3. Starts background loading of `MSBuildWorkspace` from the `.slnx` file:
   - `MSBuildWorkspace.Create()`
   - `workspace.OpenSolutionAsync(solutionPath)`
   - `Compilation` objects obtained per-project lazily via `project.GetCompilationAsync()`
   - When done: refreshes type map, call graph, inheritance tree, persists all to durable state
   - Sets `_fullWorkspaceReady = true`

**Reentrancy model:** The grain is marked `[Reentrant]` to allow the background workspace load to proceed while the grain serves cached-data queries. Methods that need the full semantic model (AnalyzeSemantics, Rename, ImplementInterface) check `_fullWorkspaceReady`:
- If ready: proceed with the compilation
- If not ready: return immediately with a status message ("Workspace loading, try again in a few seconds") rather than blocking — the caller (Thread LLM or orchestrator) can retry

This avoids the Orleans single-threaded deadlock: the background `OpenSolutionAsync` runs as an awaited task within the grain's scheduler, and concurrent calls are interleaved thanks to `[Reentrant]`.

**MSBuild dependency:** `Microsoft.CodeAnalysis.MSBuild` + `Microsoft.Build.Locator` packages. `MSBuildLocator.RegisterDefaults()` called once at silo startup (in `IAW.Assistant/Program.cs`) AND in the test fixture (`AgentTestSiloConfigurator`) for test coverage.

**Cache invalidation:** When files change (via `IReceiver<CodeChangedMessage>` from Git/DotNet agents), the workspace is reloaded. Type map in durable state is always the fallback.

**What lives where:**

| Data | Storage | Survives deactivation? |
|------|---------|----------------------|
| Type map (types, methods, properties, files) | Durable state (JSON) | Yes |
| Call graph (caller-to-callee edges) | Durable state (JSON) | Yes |
| Inheritance tree (type-to-base, type-to-interfaces) | Durable state (JSON) | Yes |
| Solution path | Durable state | Yes |
| `MSBuildWorkspace` object | In-memory only | No — rebuilt on activation |
| `Compilation` per project | In-memory only | No — lazy-loaded on demand |
| `SemanticModel` per file | In-memory only | No — obtained from compilation |

### 2. Agent-to-Agent Communication

```
GitAgent ──CodeChangedMessage──> RoslynAgent (IReceiver, P2P direct)
  After commit: publishes          On receive: reload workspace,
  changed file paths               refresh type map, persist

DotNetAgent ──TestResultMessage──> RoslynAgent (IReceiver, P2P direct)
  After test: publishes            On receive: track pass/fail state
  results

DotNetAgent ──CodeChangedMessage──> RoslynAgent (IReceiver, P2P direct)
  After format: publishes          On receive: re-index changed files
  changed file paths

RoslynAgent ──"workspace.reindexed"──> Stream (pub/sub, loose coupling)
  After reindex completes            Consumers: INuGet (re-check packages),
                                     IDotNet (auto-format), Thread (notify user)

CodeOrchestrator ──IGit.CommitAsync()──> GitAgent (direct call)
  After writing code                   Triggers chain: commit -> CodeChanged -> reindex
```

**Communication patterns:**

| From | To | Mechanism | Why this pattern |
|------|----|-----------|-----------------|
| GitAgent | RoslynAgent | `IReceiver<CodeChangedMessage>` (P2P) | Tight — Roslyn must know about changes |
| DotNetAgent | RoslynAgent | `IReceiver<TestResultMessage>` (P2P) | Tight — Roslyn tracks test state |
| DotNetAgent | RoslynAgent | `IReceiver<CodeChangedMessage>` (P2P) | Format changes need re-index |
| RoslynAgent | anyone | Stream `"workspace.reindexed"` (pub/sub) | Loose — multiple consumers may react |
| CodeOrchestrator | GitAgent | Direct call | Triggers the reindex chain |

### 3. Call Graph & Inheritance Tree

**Call Graph** — `Dictionary<string, List<string>>` mapping fully qualified method name to its callees. Built by walking each method body's `InvocationExpressionSyntax` nodes and resolving target symbols via `SemanticModel.GetSymbolInfo()`. Requires full compilation.

**Inheritance Tree** — `Dictionary<string, InheritanceInfo>` mapping type name to base type, implemented interfaces, and derived types (reverse-indexed). Built from `INamedTypeSymbol.BaseType` and `INamedTypeSymbol.Interfaces`.

Both are built after workspace loads (same background task), persisted to durable state, and refreshed on `CodeChangedMessage`.

**Query tools:**

| Tool | Input | Returns |
|------|-------|---------|
| `GetCallersOf` | methodName | All methods that call this method |
| `GetCalleesOf` | methodName | All methods called by this method |
| `GetImplementors` | interfaceName | All classes implementing this interface |
| `GetBaseTypes` | className | Inheritance chain up to object |
| `GetOverrides` | methodName | All overrides of a virtual/abstract method |

### 4. Code Modification Tools (Level 2 — Targeted Edits)

Surgical Roslyn-powered operations using `SyntaxFactory` + `DocumentEditor`:

| Tool | Input | What it does |
|------|-------|-------------|
| `AddMethod` | filePath, className, signature, body | Parses file, finds class, inserts method, formats, writes back |
| `AddProperty` | filePath, className, type, name | Adds auto-property or full property |
| `AddUsing` | filePath, namespace | Adds using if not present, sorted |
| `ImplementInterface` | filePath, className, interfaceName | Uses semantic model to generate stubs for unimplemented members |
| `RemoveMember` | filePath, className, memberName | Removes method/property/field by name |
| `ModifyMethod` | filePath, className, methodName, newBody | Replaces method body preserving signature |
| `CreateFile` | filePath, namespace, typeName, typeKind, baseTypes | Generates complete C# file |
| `AddParameter` | filePath, className, methodName, paramType, paramName | Adds parameter to method |

**Internal flow for modifications:**

1. Read file content
2. Parse to SyntaxTree
3. Find target node (class, method, etc.)
4. Build new syntax via SyntaxFactory
5. Insert/replace in tree
6. Format via `Formatter.Format(node, AdhocWorkspace)` (uses AdhocWorkspace when MSBuildWorkspace not yet loaded; full workspace when available)
7. Verify modified tree parses cleanly
8. Write back to file
9. Publish `CodeChangedMessage` to trigger reindex chain

### 5. Semantic Refactoring Tools (Level 3)

**Package note:** `Microsoft.CodeAnalysis.Features` is not a redistributable NuGet package — it's internal to Visual Studio. `RenameSymbol` is available via `Renamer.RenameSymbolAsync` from `Microsoft.CodeAnalysis.Workspaces.Common` (already included transitively). The remaining refactoring tools (ExtractMethod, MoveType, InlineVariable, ChangeSignature) are implemented as **custom SyntaxRewriter-based operations** using the public Roslyn APIs — not the internal VS Features assembly.

| Tool | Input | Implementation approach |
|------|-------|------------------------|
| `RenameSymbol` | symbolName, newName, scope | `Renamer.RenameSymbolAsync()` from Workspaces.Common — handles nameof, attributes, cross-project refs |
| `ExtractMethod` | filePath, startLine, endLine, newMethodName | Custom: analyze data flow via `SemanticModel.AnalyzeDataFlow()` to find parameters/return type, build new method via `SyntaxFactory`, replace extracted lines with call |
| `MoveType` | filePath, typeName, targetFilePath | Custom: remove type from source tree, create new file with type + required usings, update all `using` directives across solution via compilation symbol lookup |
| `InlineVariable` | filePath, variableName, line | Custom: find variable's initializer, find all references via `SymbolFinder.FindReferencesAsync()`, replace each usage with the initializer expression |
| `ChangeSignature` | filePath, className, methodName, newParameters | Custom: modify method declaration, use `SymbolFinder.FindCallersAsync()` to find all call sites, update each invocation's argument list |

Level 3 tools require full solution workspace. They use `document.GetSyntaxRootAsync()` + `document.GetSemanticModelAsync()` from MSBuildWorkspace, then apply changes via `workspace.TryApplyChanges()`.

**Error handling for all modification tools:**
- Validate file exists and parses without errors before modifying
- Return the diff (what changed) on success
- Return error message on failure (class not found, parse error, etc.)
- Never write a file that doesn't parse cleanly

### 6. Folder Restructure

Reorganize `src/Agents.CSharp/` from flat files to domain subfolders, matching the pattern used in `src/Agents/` (Infrastructure/, Knowledge/, LLM/, Memory/, Orchestration/). Separate commit before any logic changes.

**Current (flat):**
```
src/Agents.CSharp/
  RoslynAgent.cs, IRoslyn.cs, DotNetAgent.cs, IDotNet.cs,
  GitHubAgent.cs, IGitHub.cs, NuGetAgent.cs, INuGet.cs,
  Tools/RoslynTools.cs, GitHub/, Models/, Prompts/
```

**Proposed:**
```
src/Agents.CSharp/
  Roslyn/
    IRoslyn.cs
    RoslynAgent.cs
    Tools/
      RoslynTools.cs
      CodeModificationTools.cs
      RefactoringTools.cs
    Workspace/
      SolutionWorkspaceManager.cs
      CallGraphBuilder.cs
      InheritanceTreeBuilder.cs
  DotNet/
    IDotNet.cs
    DotNetAgent.cs
  GitHub/
    IGitHub.cs
    GitHubAgent.cs
    GitHubService.cs
    GitHubRegistration.cs
  NuGet/
    INuGet.cs
    NuGetAgent.cs
  Models/
    PackageUpdate.cs
    ReleaseInfo.cs
  Prompts/
    CodingAgentPrompts.cs
  OrchestrationCompiler.cs
```

### What Changes Where

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Agents.CSharp/**` | Restructure | Move files to domain subfolders |
| `src/Agents.CSharp/Roslyn/RoslynAgent.cs` | Major rewrite | Workspace loading, call graph, modification methods, background indexing |
| `src/Agents.CSharp/Roslyn/IRoslyn.cs` | Expand | New methods: modification tools, refactoring tools, query tools |
| `src/Agents.CSharp/Roslyn/Tools/RoslynTools.cs` | Rewrite | Full workspace-backed analysis tools |
| `src/Agents.CSharp/Roslyn/Tools/CodeModificationTools.cs` | Create | Level 2 targeted edit implementations |
| `src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs` | Create | Level 3 semantic refactoring implementations |
| `src/Agents.CSharp/Roslyn/Workspace/SolutionWorkspaceManager.cs` | Create | MSBuildWorkspace lifecycle: load, cache, invalidate, rebuild |
| `src/Agents.CSharp/Roslyn/Workspace/CallGraphBuilder.cs` | Create | Walks semantic models to build caller-to-callee edges |
| `src/Agents.CSharp/Roslyn/Workspace/InheritanceTreeBuilder.cs` | Create | Walks type symbols to build inheritance/implementation maps |
| `src/Agents.CSharp/Agents.CSharp.csproj` | Modify | Add MSBuild, Build.Locator packages |
| `src/IAW.Assistant/Program.cs` | Modify | Call `MSBuildLocator.RegisterDefaults()` at startup |
| `src/IAW.Testing/AgentTest.cs` | Modify | Call `MSBuildLocator.RegisterDefaults()` in test fixture for workspace tests |
| `src/Core/Communication/Messages/CodeChangedMessage.cs` | Modify | Change `string FilePath` to `IReadOnlyList<string> FilePaths` for batched invalidation (Core contract change) |
| `src/Agents/Infrastructure/GitAgent.cs` | Modify | Publish `CodeChangedMessage` to RoslynAgent after commits (send P2P via `IReceiver<CodeChangedMessage>`) |
| `src/Agents.CSharp/DotNet/DotNetAgent.cs` | Modify | Publish `CodeChangedMessage` after `dotnet format` modifies files |

### New NuGet Packages

| Package | Purpose |
|---------|---------|
| `Microsoft.CodeAnalysis.MSBuild` | Load .slnx/.sln with full NuGet/project resolution |
| `Microsoft.Build.Locator` | Find MSBuild installation at runtime |

Note: `Microsoft.CodeAnalysis.Features` is NOT used — it's not redistributable. Level 3 refactoring is implemented via custom SyntaxRewriter operations using public Roslyn APIs (`Renamer`, `SymbolFinder`, `SyntaxFactory`, `SemanticModel.AnalyzeDataFlow`).

### What Does NOT Change

- Agent base class (no core changes)
- ThreadAgent, AgentSelector, CodeOrchestrator (used as-is)
- Client layer (Telegram, MCP, DevUI)
- Other agents (Shell, Memory, LLM agents)

### Scope Boundary

This spec covers Roslyn intelligence only. Smart NuGet evolution, reactive auto-format, and orchestration performance are separate specs that build on this foundation.
