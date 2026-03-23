# Enhanced Roslyn Intelligence — Foundation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform RoslynAgent from a read-only syntax parser into a full solution-aware code intelligence engine with MSBuildWorkspace, call graphs, inheritance trees, and reactive reindexing.

**Architecture:** RoslynAgent loads the solution via MSBuildWorkspace on activation, builds type map / call graph / inheritance tree in the background, persists metadata to durable state for instant warmup after deactivation. Subscribes to `"code.changed"` stream for invalidation. Marked `[Reentrant]` to avoid Orleans deadlocks during background load.

**Tech Stack:** C# / .NET 11, Orleans 10, Microsoft.CodeAnalysis.MSBuild, Microsoft.Build.Locator, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-20-enhanced-roslyn-intelligence-design.md`

**Scope:** This is Plan A (foundation). Plan B (code modification + refactoring tools) is a separate document that builds on this.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Agents.CSharp/**` | Restructure | Move flat files to domain subfolders |
| `src/Agents.CSharp/Roslyn/Workspace/SolutionWorkspaceManager.cs` | Create | MSBuildWorkspace lifecycle: load solution, cache compilations, reload on invalidation |
| `src/Agents.CSharp/Roslyn/Workspace/CallGraphBuilder.cs` | Create | Walk semantic models, build caller→callee dictionary |
| `src/Agents.CSharp/Roslyn/Workspace/InheritanceTreeBuilder.cs` | Create | Walk type symbols, build base/interface/derived maps |
| `src/Agents.CSharp/Roslyn/IRoslyn.cs` | Expand | Add query methods: GetCallersOf, GetCalleesOf, GetImplementors, GetBaseTypes, GetOverrides |
| `src/Agents.CSharp/Roslyn/RoslynAgent.cs` | Rewrite | Integrate workspace manager, background loading, stream subscription, new query methods |
| `src/Agents.CSharp/Roslyn/Tools/RoslynTools.cs` | Rewrite | Workspace-backed semantic analysis |
| `src/Agents.CSharp/Agents.CSharp.csproj` | Modify | Add MSBuild + Build.Locator packages |
| `src/Core/Communication/Messages/CodeChangedMessage.cs` | Modify | Add `FilePaths` list property |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | Modify | Publish CodeChangedMessage to stream after writing files |
| `src/Agents.CSharp/DotNet/DotNetAgent.cs` | Modify | Publish CodeChangedMessage to stream after format |
| `src/IAW.Assistant/Program.cs` | Modify | Register MSBuildLocator at startup |

---

### Task 1: Folder Restructure

Move `src/Agents.CSharp/` from flat files to domain subfolders. Pure `git mv` — no logic changes.

**Files:**
- Modify: `src/Agents.CSharp/` (move files)

- [ ] **Step 1: Create target directories**

```bash
mkdir -p src/Agents.CSharp/Roslyn/Tools src/Agents.CSharp/Roslyn/Workspace
mkdir -p src/Agents.CSharp/DotNet src/Agents.CSharp/NuGet
```

- [ ] **Step 2: Move files**

```bash
# Roslyn
git mv src/Agents.CSharp/IRoslyn.cs src/Agents.CSharp/Roslyn/
git mv src/Agents.CSharp/RoslynAgent.cs src/Agents.CSharp/Roslyn/
git mv src/Agents.CSharp/Tools/RoslynTools.cs src/Agents.CSharp/Roslyn/Tools/

# DotNet
git mv src/Agents.CSharp/IDotNet.cs src/Agents.CSharp/DotNet/
git mv src/Agents.CSharp/DotNetAgent.cs src/Agents.CSharp/DotNet/

# NuGet
git mv src/Agents.CSharp/INuGet.cs src/Agents.CSharp/NuGet/
git mv src/Agents.CSharp/NuGetAgent.cs src/Agents.CSharp/NuGet/

# GitHub — already has a subfolder, move agents into it
git mv src/Agents.CSharp/IGitHub.cs src/Agents.CSharp/GitHub/
git mv src/Agents.CSharp/GitHubAgent.cs src/Agents.CSharp/GitHub/

# Remove now-empty Tools dir
rmdir src/Agents.CSharp/Tools 2>/dev/null || true
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Agents.CSharp/Agents.CSharp.csproj`
Expected: Build succeeded. Namespaces don't depend on folder paths in this project — `RootNamespace` is set in .csproj.

- [ ] **Step 4: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: restructure Agents.CSharp into domain subfolders (Roslyn, DotNet, GitHub, NuGet)"
```

---

### Task 2: Add NuGet Packages and MSBuildLocator Registration

Add `Microsoft.CodeAnalysis.MSBuild` and `Microsoft.Build.Locator` to the project, register MSBuildLocator at silo startup.

**Files:**
- Modify: `src/Agents.CSharp/Agents.CSharp.csproj`
- Modify: `src/IAW.Assistant/Program.cs`
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add packages to Directory.Packages.props**

Look up latest stable versions of `Microsoft.CodeAnalysis.MSBuild` and `Microsoft.Build.Locator` via Context7. Add `<PackageVersion>` entries.

- [ ] **Step 2: Add PackageReference to Agents.CSharp.csproj**

Add after the existing `Microsoft.CodeAnalysis.CSharp.Workspaces` line:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.MSBuild" />
<PackageReference Include="Microsoft.Build.Locator" />
```

- [ ] **Step 3: Register MSBuildLocator at startup**

In `src/IAW.Assistant/Program.cs`, add before any other code:

```csharp
Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
```

Add `using Microsoft.Build.Locator;` if needed. This MUST be called before any Roslyn workspace types are loaded.

- [ ] **Step 4: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/Agents.CSharp/Agents.CSharp.csproj src/IAW.Assistant/Program.cs
git commit -m "feat: add Microsoft.CodeAnalysis.MSBuild and Build.Locator packages"
```

---

### Task 3: Create SolutionWorkspaceManager

The core component that manages MSBuildWorkspace lifecycle.

**Files:**
- Create: `src/Agents.CSharp/Roslyn/Workspace/SolutionWorkspaceManager.cs`
- Test: `test/Core.Tests/SolutionWorkspaceManagerTests.cs`

- [ ] **Step 1: Write test**

```csharp
using Xunit;

namespace IAW.Core.Tests;

public class SolutionWorkspaceManagerTests
{
    [Fact]
    public void IsReady_BeforeLoad_ReturnsFalse()
    {
        var manager = new IAW.Agents.CSharp.Roslyn.Workspace.SolutionWorkspaceManager();
        Assert.False(manager.IsReady);
    }

    [Fact]
    public void GetCompilation_BeforeLoad_ReturnsNull()
    {
        var manager = new IAW.Agents.CSharp.Roslyn.Workspace.SolutionWorkspaceManager();
        Assert.Null(manager.GetCompilation("SomeProject"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~SolutionWorkspaceManagerTests" -v m`
Expected: FAIL — class does not exist.

- [ ] **Step 3: Implement SolutionWorkspaceManager**

```csharp
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace IAW.Agents.CSharp.Roslyn.Workspace;

public sealed class SolutionWorkspaceManager : IDisposable
{
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private readonly ConcurrentDictionary<string, Compilation> _compilationCache = new();

    public bool IsReady => _solution is not null;
    public Solution? Solution => _solution;

    public async Task LoadSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        _compilationCache.Clear();
        _workspace?.Dispose();

        _workspace = MSBuildWorkspace.Create();
        _solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
    }

    public Compilation? GetCompilation(string projectName)
    {
        if (_solution is null) return null;
        if (_compilationCache.TryGetValue(projectName, out var cached)) return cached;

        var project = _solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        if (project is null) return null;

        var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
        if (compilation is not null)
            _compilationCache[projectName] = compilation;
        return compilation;
    }

    public async Task<Compilation?> GetCompilationAsync(string projectName, CancellationToken ct = default)
    {
        if (_solution is null) return null;
        if (_compilationCache.TryGetValue(projectName, out var cached)) return cached;

        var project = _solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        if (project is null) return null;

        var compilation = await project.GetCompilationAsync(ct);
        if (compilation is not null)
            _compilationCache[projectName] = compilation;
        return compilation;
    }

    public IEnumerable<string> GetProjectNames() =>
        _solution?.Projects.Select(p => p.Name) ?? [];

    public async Task ReloadAsync(string solutionPath, CancellationToken ct = default)
    {
        await LoadSolutionAsync(solutionPath, ct);
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~SolutionWorkspaceManagerTests" -v m`
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Workspace/SolutionWorkspaceManager.cs test/Core.Tests/SolutionWorkspaceManagerTests.cs
git commit -m "feat: create SolutionWorkspaceManager for MSBuildWorkspace lifecycle"
```

---

### Task 4: Create CallGraphBuilder

Pure function: takes compilations, returns caller→callee dictionary.

**Files:**
- Create: `src/Agents.CSharp/Roslyn/Workspace/CallGraphBuilder.cs`
- Test: `test/Core.Tests/CallGraphBuilderTests.cs`

- [ ] **Step 1: Write test**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IAW.Core.Tests;

public class CallGraphBuilderTests
{
    [Fact]
    public void Build_SimpleCall_FindsEdge()
    {
        var source = """
            namespace Test;
            public class Foo
            {
                public void A() { B(); }
                public void B() { }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var graph = IAW.Agents.CSharp.Roslyn.Workspace.CallGraphBuilder.Build([compilation]);

        Assert.True(graph.ContainsKey("Test.Foo.A"));
        Assert.Contains("Test.Foo.B", graph["Test.Foo.A"]);
    }

    [Fact]
    public void Build_NoCall_EmptyCallees()
    {
        var source = """
            namespace Test;
            public class Foo
            {
                public void A() { }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var graph = IAW.Agents.CSharp.Roslyn.Workspace.CallGraphBuilder.Build([compilation]);

        Assert.True(!graph.ContainsKey("Test.Foo.A") || graph["Test.Foo.A"].Count == 0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

- [ ] **Step 3: Implement CallGraphBuilder**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IAW.Agents.CSharp.Roslyn.Workspace;

public static class CallGraphBuilder
{
    public static Dictionary<string, List<string>> Build(IEnumerable<Compilation> compilations)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var compilation in compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var methodSymbol = model.GetDeclaredSymbol(method);
                    if (methodSymbol is null) continue;

                    var callerKey = GetFullyQualifiedName(methodSymbol);
                    var callees = new List<string>();

                    foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var symbolInfo = model.GetSymbolInfo(invocation);
                        if (symbolInfo.Symbol is IMethodSymbol targetMethod)
                            callees.Add(GetFullyQualifiedName(targetMethod));
                    }

                    if (callees.Count > 0)
                        graph[callerKey] = callees.Distinct().ToList();
                }
            }
        }

        return graph;
    }

    public static Dictionary<string, List<string>> BuildReverseGraph(Dictionary<string, List<string>> forwardGraph)
    {
        var reverse = new Dictionary<string, List<string>>();
        foreach (var (caller, callees) in forwardGraph)
        {
            foreach (var callee in callees)
            {
                if (!reverse.TryGetValue(callee, out var callers))
                {
                    callers = [];
                    reverse[callee] = callers;
                }
                callers.Add(caller);
            }
        }
        return reverse;
    }

    private static string GetFullyQualifiedName(ISymbol symbol)
    {
        var parts = new List<string>();
        var current = symbol;
        while (current is not null and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            parts.Add(current.Name);
            current = current.ContainingSymbol;
        }
        parts.Reverse();
        return string.Join(".", parts);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CallGraphBuilderTests" -v m`
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Workspace/CallGraphBuilder.cs test/Core.Tests/CallGraphBuilderTests.cs
git commit -m "feat: create CallGraphBuilder for caller-callee analysis"
```

---

### Task 5: Create InheritanceTreeBuilder

Pure function: takes compilations, returns inheritance/implementation maps.

**Files:**
- Create: `src/Agents.CSharp/Roslyn/Workspace/InheritanceTreeBuilder.cs`
- Test: `test/Core.Tests/InheritanceTreeBuilderTests.cs`

- [ ] **Step 1: Write test**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IAW.Core.Tests;

public class InheritanceTreeBuilderTests
{
    [Fact]
    public void Build_ClassImplementsInterface_Found()
    {
        var source = """
            namespace Test;
            public interface IFoo { }
            public class Foo : IFoo { }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var result = IAW.Agents.CSharp.Roslyn.Workspace.InheritanceTreeBuilder.Build([compilation]);

        Assert.True(result.ContainsKey("Test.Foo"));
        Assert.Contains("Test.IFoo", result["Test.Foo"].Interfaces);
    }

    [Fact]
    public void Build_DerivedTypes_ReverseIndexed()
    {
        var source = """
            namespace Test;
            public interface IFoo { }
            public class Foo : IFoo { }
            public class Bar : IFoo { }
            """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var result = IAW.Agents.CSharp.Roslyn.Workspace.InheritanceTreeBuilder.Build([compilation]);

        Assert.True(result.ContainsKey("Test.IFoo"));
        Assert.Contains("Test.Foo", result["Test.IFoo"].DerivedTypes);
        Assert.Contains("Test.Bar", result["Test.IFoo"].DerivedTypes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

- [ ] **Step 3: Implement InheritanceTreeBuilder**

```csharp
using Microsoft.CodeAnalysis;

namespace IAW.Agents.CSharp.Roslyn.Workspace;

public record InheritanceInfo(
    string? BaseType,
    List<string> Interfaces,
    List<string> DerivedTypes);

public static class InheritanceTreeBuilder
{
    public static Dictionary<string, InheritanceInfo> Build(IEnumerable<Compilation> compilations)
    {
        var tree = new Dictionary<string, InheritanceInfo>();

        foreach (var compilation in compilations)
        {
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot();

                foreach (var node in root.DescendantNodes())
                {
                    if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol) continue;
                    if (typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct)) continue;

                    var fullName = GetFullName(typeSymbol);
                    var baseType = typeSymbol.BaseType is { SpecialType: not SpecialType.System_Object }
                        ? GetFullName(typeSymbol.BaseType)
                        : null;
                    var interfaces = typeSymbol.Interfaces
                        .Select(GetFullName)
                        .ToList();

                    tree[fullName] = new InheritanceInfo(baseType, interfaces, []);
                }
            }
        }

        // reverse index: populate DerivedTypes
        foreach (var (typeName, info) in tree)
        {
            if (info.BaseType is not null && tree.TryGetValue(info.BaseType, out var baseInfo))
                baseInfo.DerivedTypes.Add(typeName);

            foreach (var iface in info.Interfaces)
            {
                if (tree.TryGetValue(iface, out var ifaceInfo))
                    ifaceInfo.DerivedTypes.Add(typeName);
            }
        }

        return tree;
    }

    private static string GetFullName(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.IsGlobalNamespace == true
            ? ""
            : symbol.ContainingNamespace?.ToDisplayString() ?? "";
        return string.IsNullOrEmpty(ns) ? symbol.Name : $"{ns}.{symbol.Name}";
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~InheritanceTreeBuilderTests" -v m`
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Workspace/InheritanceTreeBuilder.cs test/Core.Tests/InheritanceTreeBuilderTests.cs
git commit -m "feat: create InheritanceTreeBuilder for type hierarchy analysis"
```

---

### Task 6: Update CodeChangedMessage Contract

Add `FilePaths` list to support batched invalidation. Keep backward compat with existing `FilePath`.

**Files:**
- Modify: `src/Core/Communication/Messages/CodeChangedMessage.cs`

- [ ] **Step 1: Update the record**

Current:
```csharp
public record CodeChangedMessage(
    [property: Id(0)] string ProjectPath,
    [property: Id(1)] string FilePath,
    [property: Id(2)] string Description) : IAgentMessage
```

Change to:
```csharp
public record CodeChangedMessage(
    [property: Id(0)] string ProjectPath,
    [property: Id(1)] string FilePath,
    [property: Id(2)] string Description) : IAgentMessage
{
    [Id(3)] public string SourceAgentId { get; init; } = string.Empty;
    [Id(4)] public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    [Id(5)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    [Id(6)] public IReadOnlyList<string> FilePaths { get; init; } = FilePath is not null ? [FilePath] : [];
}
```

This preserves backward compat — existing code that passes `FilePath` still works, and `FilePaths` auto-populates from it. New code can set `FilePaths` directly.

- [ ] **Step 2: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add src/Core/Communication/Messages/CodeChangedMessage.cs
git commit -m "feat: add FilePaths list to CodeChangedMessage for batched invalidation"
```

---

### Task 7: Rewrite RoslynAgent with Workspace Integration

The big one. Integrate SolutionWorkspaceManager, add `[Reentrant]`, background loading, stream subscription, and new query methods.

**Files:**
- Modify: `src/Agents.CSharp/Roslyn/IRoslyn.cs`
- Rewrite: `src/Agents.CSharp/Roslyn/RoslynAgent.cs`
- Rewrite: `src/Agents.CSharp/Roslyn/Tools/RoslynTools.cs`
- Test: `test/Core.Tests/RoslynAgentTests.cs`

- [ ] **Step 1: Expand IRoslyn interface**

Add to `IRoslyn.cs` after existing methods:

```csharp
// Call graph queries
Task<string> GetCallersOfAsync(string methodName, CancellationToken ct = default);
Task<string> GetCalleesOfAsync(string methodName, CancellationToken ct = default);

// Inheritance queries
Task<string> GetImplementorsAsync(string interfaceName, CancellationToken ct = default);
Task<string> GetBaseTypesAsync(string className, CancellationToken ct = default);
Task<string> GetOverridesAsync(string methodName, CancellationToken ct = default);

// Workspace status
Task<string> GetWorkspaceStatusAsync(CancellationToken ct = default);
```

Update description and capabilities to reflect new abilities.

- [ ] **Step 2: Rewrite RoslynAgent**

Key changes to `RoslynAgent.cs`:
- Add `[Reentrant]` attribute to the class
- Add `IStreamConsumer<CodeChangedMessage>` implementation
- Add `SolutionWorkspaceManager` field
- Override `OnActivateAsync` to start background workspace load
- Implement new query methods using cached call graph and inheritance tree from durable state
- Methods requiring full semantic model return "Workspace loading" if not ready

The agent keeps all existing methods (GetTypeMapAsync, FindReferencesAsync, etc.) but upgrades them to use the workspace when available.

- [ ] **Step 3: Rewrite RoslynTools**

Update `RoslynTools.cs` to accept a `SolutionWorkspaceManager` reference and use its compilations for semantic analysis instead of the minimal single-reference compilation.

- [ ] **Step 4: Write tests**

Create `test/Core.Tests/RoslynAgentTests.cs` extending `AgentTest<RoslynAgent>`:

```csharp
[Fact]
public async Task GetWorkspaceStatus_ReturnsStatus()
{
    var ct = TestContext.Current.CancellationToken;
    var roslyn = Agent(UniqueId("roslyn-status"));
    var status = await roslyn.GetResponse("workspace status", ct);
    Assert.NotNull(status);
}

[Fact]
public async Task GetTypeMap_ReturnsTypes()
{
    var ct = TestContext.Current.CancellationToken;
    var roslyn = Agent(UniqueId("roslyn-typemap"));
    await roslyn.SetWorkspace(workspacePath, ct);
    var result = await ((IRoslyn)roslyn).GetTypeMapAsync(ct);
    Assert.NotNull(result);
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All pass.

- [ ] **Step 6: Commit**

```bash
git add src/Agents.CSharp/Roslyn/ test/Core.Tests/RoslynAgentTests.cs
git commit -m "feat: rewrite RoslynAgent with MSBuildWorkspace, call graph, and inheritance tree"
```

---

### Task 8: Wire CodeChangedMessage Publishers

Add stream publishing to CodeOrchestrator (after writing files) and DotNetAgent (after formatting).

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`
- Modify: `src/Agents.CSharp/DotNet/DotNetAgent.cs`

- [ ] **Step 1: Add publishing to CodeOrchestratorAgent**

In `ExecuteCodeOrchestration`, after writing files and before the git commit call, add:

```csharp
await PublishToStreamAsync("code.changed", new CodeChangedMessage(
    taskDir, "", "Code orchestration completed") { FilePaths = writtenFiles });
```

The `writtenFiles` list should be collected during file writes in the orchestration.

- [ ] **Step 2: Add publishing to DotNetAgent**

In `RunFormatAsync`, after `dotnet format` completes and `changedFiles` are parsed, add:

```csharp
if (changedFiles.Count > 0)
{
    await PublishToStreamAsync("code.changed", new CodeChangedMessage(
        solutionPath, "", "dotnet format completed") { FilePaths = changedFiles });
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build IAW.slnx && dotnet test IAW.slnx -v m`
Expected: Build succeeded, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs src/Agents.CSharp/DotNet/DotNetAgent.cs
git commit -m "feat: wire CodeChangedMessage publishing from CodeOrchestrator and DotNetAgent"
```

---

### Task 9: Full Build, Test, and Integration Verification

**Files:** None (verification only)

- [ ] **Step 1: Build the solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors, 0 warnings (excluding suppressed).

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All pass.

- [ ] **Step 3: Start Aspire and verify**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Verify via Aspire MCP tools that assistant starts without errors and RoslynAgent registers in AgentRegistry.

- [ ] **Step 4: Test via MCP**

Send via MCP `assistant_chat`: "what's the workspace status of the roslyn agent?"
Expected: Thread delegates to Roslyn, returns workspace status.

- [ ] **Step 5: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve integration issues from Roslyn foundation changes"
```
