# Roslyn Code Modification & Refactoring Tools — Plan B

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add code modification (Level 2) and semantic refactoring (Level 3) tools to RoslynAgent so IAW agents can write and transform C# code structurally via syntax trees, not text edits.

**Architecture:** Two new tool classes registered via `DefineTools()` in RoslynAgent. `CodeModificationTools` handles targeted edits (AddMethod, AddUsing, etc.) using `SyntaxFactory` + `Formatter`. `RefactoringTools` handles semantic operations (RenameSymbol, ExtractMethod) using `Renamer` + custom `SyntaxRewriter` implementations. Both publish `CodeChangedMessage` after writes.

**Tech Stack:** C# / .NET 11, Microsoft.CodeAnalysis.CSharp (SyntaxFactory, Formatter, Renamer, SymbolFinder, DataFlowAnalysis), Orleans 10, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-20-enhanced-roslyn-intelligence-design.md` (Sections 4 & 5)

**Prerequisite:** Plan A (foundation) must be complete — SolutionWorkspaceManager, CallGraphBuilder, InheritanceTreeBuilder, [Reentrant] RoslynAgent all in place.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Agents.CSharp/Roslyn/Tools/CodeModificationTools.cs` | Create | Level 2: AddMethod, AddProperty, AddUsing, RemoveMember, ModifyMethod, CreateFile, AddParameter |
| `src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs` | Create | Level 3: RenameSymbol, ExtractMethod, MoveType, InlineVariable, ChangeSignature |
| `src/Agents.CSharp/Roslyn/IRoslyn.cs` | Modify | Add ImplementInterface method (needs semantic model) |
| `src/Agents.CSharp/Roslyn/RoslynAgent.cs` | Modify | Register new tool classes in DefineTools(), add ImplementInterface |
| `test/Core.Tests/CodeModificationToolsTests.cs` | Create | Tests for Level 2 tools |
| `test/Core.Tests/RefactoringToolsTests.cs` | Create | Tests for Level 3 tools |

---

### Task 1: CodeModificationTools — Core Infrastructure

Create the tool class with the shared modification flow (read → parse → transform → format → verify → write) and the first 3 tools: AddUsing, CreateFile, AddMethod.

**CRITICAL: Use Context7 to look up `Microsoft.CodeAnalysis.CSharp.SyntaxFactory`, `Microsoft.CodeAnalysis.Formatting.Formatter`, and `Microsoft.CodeAnalysis.CSharp.Syntax` APIs before writing any code.**

**Files:**
- Create: `src/Agents.CSharp/Roslyn/Tools/CodeModificationTools.cs`
- Create: `test/Core.Tests/CodeModificationToolsTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using Xunit;

namespace IAW.Core.Tests;

public class CodeModificationToolsTests
{
    private readonly string _tempDir;
    private readonly IAW.Agents.CSharp.Roslyn.Tools.CodeModificationTools _tools;

    public CodeModificationToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"roslyn-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tools = new IAW.Agents.CSharp.Roslyn.Tools.CodeModificationTools(() => _tempDir);
    }

    [Fact]
    public async Task CreateFile_GeneratesValidCSharp()
    {
        var result = await _tools.CreateFileAsync(
            Path.Combine(_tempDir, "Foo.cs"), "Test.Namespace", "Foo", "class", "");
        Assert.Contains("class Foo", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "Foo.cs")));
    }

    [Fact]
    public async Task AddUsing_AddsWhenMissing()
    {
        var filePath = Path.Combine(_tempDir, "Bar.cs");
        await File.WriteAllTextAsync(filePath, "namespace Test;\npublic class Bar { }");
        var result = await _tools.AddUsingAsync(filePath, "System.Text");
        Assert.Contains("System.Text", result);
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("using System.Text;", content);
    }

    [Fact]
    public async Task AddUsing_SkipsWhenPresent()
    {
        var filePath = Path.Combine(_tempDir, "Baz.cs");
        await File.WriteAllTextAsync(filePath, "using System.Text;\nnamespace Test;\npublic class Baz { }");
        var result = await _tools.AddUsingAsync(filePath, "System.Text");
        Assert.Contains("already", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddMethod_InsertsIntoClass()
    {
        var filePath = Path.Combine(_tempDir, "MyClass.cs");
        await File.WriteAllTextAsync(filePath, "namespace Test;\npublic class MyClass\n{\n}");
        var result = await _tools.AddMethodAsync(filePath, "MyClass",
            "public void DoWork()", "Console.WriteLine(\"hello\");");
        Assert.Contains("DoWork", result);
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("void DoWork", content);
        Assert.Contains("Console.WriteLine", content);
    }
}
```

- [ ] **Step 2: Implement CodeModificationTools**

The class should have:
- Constructor taking `Func<string> getWorkspacePath` (same pattern as RoslynTools)
- Private helper: `ModifyFileAsync(filePath, Func<SyntaxNode, SyntaxNode> transform)` — the shared read/parse/transform/format/verify/write flow
- `[Description]` attributes on all public methods for AI tool registration
- Each method returns a string describing what changed (or error message)

Tools to implement in this task:
1. **CreateFileAsync** — builds a CompilationUnit with namespace, type declaration, optional base types via SyntaxFactory
2. **AddUsingAsync** — parses file, checks if using exists, adds sorted if missing
3. **AddMethodAsync** — parses file, finds class by name, parses method signature + body, inserts as member

Use `Formatter.Format(root, new AdhocWorkspace())` for formatting.

- [ ] **Step 3: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CodeModificationToolsTests" -v m`
Expected: All 4 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Tools/CodeModificationTools.cs test/Core.Tests/CodeModificationToolsTests.cs
git commit -m "feat: add CodeModificationTools — CreateFile, AddUsing, AddMethod"
```

---

### Task 2: CodeModificationTools — Remaining Level 2 Tools

Add AddProperty, RemoveMember, ModifyMethod, AddParameter to CodeModificationTools.

**CRITICAL: Use Context7 to look up SyntaxFactory APIs for PropertyDeclarationSyntax, ParameterSyntax before writing code.**

**Files:**
- Modify: `src/Agents.CSharp/Roslyn/Tools/CodeModificationTools.cs`
- Modify: `test/Core.Tests/CodeModificationToolsTests.cs`

- [ ] **Step 1: Add tests**

```csharp
[Fact]
public async Task AddProperty_InsertsAutoProperty()
{
    var filePath = Path.Combine(_tempDir, "PropClass.cs");
    await File.WriteAllTextAsync(filePath, "namespace Test;\npublic class PropClass\n{\n}");
    var result = await _tools.AddPropertyAsync(filePath, "PropClass", "string", "Name");
    Assert.Contains("Name", result);
    var content = await File.ReadAllTextAsync(filePath);
    Assert.Contains("string Name", content);
}

[Fact]
public async Task RemoveMember_RemovesMethod()
{
    var filePath = Path.Combine(_tempDir, "RemClass.cs");
    await File.WriteAllTextAsync(filePath,
        "namespace Test;\npublic class RemClass\n{\n    public void ToRemove() { }\n    public void ToKeep() { }\n}");
    var result = await _tools.RemoveMemberAsync(filePath, "RemClass", "ToRemove");
    Assert.Contains("Removed", result);
    var content = await File.ReadAllTextAsync(filePath);
    Assert.DoesNotContain("ToRemove", content);
    Assert.Contains("ToKeep", content);
}

[Fact]
public async Task ModifyMethod_ReplacesBody()
{
    var filePath = Path.Combine(_tempDir, "ModClass.cs");
    await File.WriteAllTextAsync(filePath,
        "namespace Test;\npublic class ModClass\n{\n    public int Get() { return 1; }\n}");
    var result = await _tools.ModifyMethodAsync(filePath, "ModClass", "Get", "return 42;");
    Assert.Contains("Modified", result);
    var content = await File.ReadAllTextAsync(filePath);
    Assert.Contains("return 42;", content);
}

[Fact]
public async Task AddParameter_AddsToMethod()
{
    var filePath = Path.Combine(_tempDir, "ParamClass.cs");
    await File.WriteAllTextAsync(filePath,
        "namespace Test;\npublic class ParamClass\n{\n    public void Run() { }\n}");
    var result = await _tools.AddParameterAsync(filePath, "ParamClass", "Run", "string", "name");
    Assert.Contains("name", result);
    var content = await File.ReadAllTextAsync(filePath);
    Assert.Contains("string name", content);
}
```

- [ ] **Step 2: Implement the 4 tools**

1. **AddPropertyAsync** — find class, create `PropertyDeclarationSyntax` with auto-accessor `{ get; set; }`, insert
2. **RemoveMemberAsync** — find class, find member by name (method/property/field), remove from members list
3. **ModifyMethodAsync** — find class, find method by name, parse new body as `BlockSyntax`, replace method body
4. **AddParameterAsync** — find class, find method, add `ParameterSyntax` to parameter list

- [ ] **Step 3: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CodeModificationToolsTests" -v m`
Expected: All 8 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Tools/CodeModificationTools.cs test/Core.Tests/CodeModificationToolsTests.cs
git commit -m "feat: add AddProperty, RemoveMember, ModifyMethod, AddParameter tools"
```

---

### Task 3: Register CodeModificationTools in RoslynAgent

Wire the new tools into RoslynAgent's DefineTools() so the LLM can use them.

**Files:**
- Modify: `src/Agents.CSharp/Roslyn/RoslynAgent.cs`

- [ ] **Step 1: Read current DefineTools**

Read `src/Agents.CSharp/Roslyn/RoslynAgent.cs` and find the `DefineTools()` method.

- [ ] **Step 2: Register CodeModificationTools**

Add after the existing `RegisterToolMethods(tools, new Tools.RoslynTools(...))` line:

```csharp
RegisterToolMethods(tools, new Tools.CodeModificationTools(getWorkspace));
```

- [ ] **Step 3: Build and test**

Run: `dotnet build IAW.slnx && dotnet test test/Core.Tests --filter "FullyQualifiedName~Roslyn" -v m`
Expected: Build succeeded, all Roslyn tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Agents.CSharp/Roslyn/RoslynAgent.cs
git commit -m "feat: register CodeModificationTools in RoslynAgent"
```

---

### Task 4: RefactoringTools — RenameSymbol

The first Level 3 tool. Uses `Renamer.RenameSymbolAsync()` from the public Roslyn API.

**CRITICAL: Use Context7 to look up `Microsoft.CodeAnalysis.Rename.Renamer` and `Microsoft.CodeAnalysis.FindSymbols.SymbolFinder` APIs.**

**Files:**
- Create: `src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs`
- Create: `test/Core.Tests/RefactoringToolsTests.cs`

- [ ] **Step 1: Write test**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IAW.Core.Tests;

public class RefactoringToolsTests
{
    [Fact]
    public async Task RenameSymbol_RenamesInFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"refactor-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "Test.cs");
        await File.WriteAllTextAsync(filePath, """
            namespace Test;
            public class OldName
            {
                public void Foo() { }
            }
            """);

        var tools = new IAW.Agents.CSharp.Roslyn.Tools.RefactoringTools(() => tempDir, null);
        var result = await tools.RenameSymbolAsync("OldName", "NewName", filePath);
        Assert.Contains("NewName", result);
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("class NewName", content);
        Assert.DoesNotContain("OldName", content);
    }
}
```

- [ ] **Step 2: Implement RefactoringTools with RenameSymbol**

RefactoringTools constructor takes `Func<string> getWorkspacePath` and `SolutionWorkspaceManager?`.

RenameSymbolAsync:
1. If workspace available: use `SymbolFinder.FindDeclarationsAsync` to find the symbol, then `Renamer.RenameSymbolAsync` for solution-wide rename
2. If workspace not available: parse the single file, find symbol declaration, use a custom `SyntaxRewriter` for file-scoped rename
3. Write modified files back, return diff summary

- [ ] **Step 3: Run test**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~RefactoringToolsTests" -v m`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs test/Core.Tests/RefactoringToolsTests.cs
git commit -m "feat: add RefactoringTools with RenameSymbol"
```

---

### Task 5: RefactoringTools — ExtractMethod and ChangeSignature

Add the two most valuable Level 3 tools.

**CRITICAL: Use Context7 to look up `SemanticModel.AnalyzeDataFlow()` and `SymbolFinder.FindCallersAsync()` APIs.**

**Files:**
- Modify: `src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs`
- Modify: `test/Core.Tests/RefactoringToolsTests.cs`

- [ ] **Step 1: Add tests**

```csharp
[Fact]
public async Task ExtractMethod_ExtractsLinesIntoNewMethod()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"extract-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    var filePath = Path.Combine(tempDir, "Extract.cs");
    await File.WriteAllTextAsync(filePath, """
        namespace Test;
        public class Calc
        {
            public int Compute()
            {
                var a = 1;
                var b = 2;
                var sum = a + b;
                return sum;
            }
        }
        """);

    var tools = new IAW.Agents.CSharp.Roslyn.Tools.RefactoringTools(() => tempDir, null);
    var result = await tools.ExtractMethodAsync(filePath, 7, 8, "AddNumbers");
    Assert.Contains("AddNumbers", result);
    var content = await File.ReadAllTextAsync(filePath);
    Assert.Contains("AddNumbers", content);
}

[Fact]
public async Task ChangeSignature_AddsParameter()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"sig-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    var filePath = Path.Combine(tempDir, "Sig.cs");
    await File.WriteAllTextAsync(filePath, """
        namespace Test;
        public class Svc
        {
            public string Greet() { return "hi"; }
        }
        """);

    var tools = new IAW.Agents.CSharp.Roslyn.Tools.RefactoringTools(() => tempDir, null);
    var result = await tools.ChangeSignatureAsync(filePath, "Svc", "Greet", "string name");
    Assert.Contains("name", result);
    var content = await File.ReadAllTextAsync(filePath);
    Assert.Contains("string name", content);
}
```

- [ ] **Step 2: Implement ExtractMethod**

1. Parse file, find method containing the line range
2. Extract the statements in the range
3. Use `SemanticModel.AnalyzeDataFlow()` on the extracted statements (if workspace available) or infer from variable declarations (if not)
4. Build new method with detected parameters and return type via SyntaxFactory
5. Replace extracted lines with a call to the new method
6. Insert new method after the containing method
7. Format and write back

- [ ] **Step 3: Implement ChangeSignature**

1. Parse file, find class and method
2. Parse new parameter string into ParameterSyntax
3. Replace method's parameter list
4. If workspace available, find all call sites via SymbolFinder and update (add default value arguments)
5. Format and write back

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~RefactoringToolsTests" -v m`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs test/Core.Tests/RefactoringToolsTests.cs
git commit -m "feat: add ExtractMethod and ChangeSignature refactoring tools"
```

---

### Task 6: RefactoringTools — MoveType and InlineVariable

The remaining Level 3 tools.

**Files:**
- Modify: `src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs`
- Modify: `test/Core.Tests/RefactoringToolsTests.cs`

- [ ] **Step 1: Add tests**

```csharp
[Fact]
public async Task MoveType_MovesToNewFile()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"move-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    var sourceFile = Path.Combine(tempDir, "Source.cs");
    await File.WriteAllTextAsync(sourceFile, """
        namespace Test;
        public class Stay { }
        public class ToMove { }
        """);
    var targetFile = Path.Combine(tempDir, "Target.cs");

    var tools = new IAW.Agents.CSharp.Roslyn.Tools.RefactoringTools(() => tempDir, null);
    var result = await tools.MoveTypeAsync(sourceFile, "ToMove", targetFile);
    Assert.Contains("Moved", result);
    Assert.True(File.Exists(targetFile));
    var sourceContent = await File.ReadAllTextAsync(sourceFile);
    Assert.DoesNotContain("ToMove", sourceContent);
    Assert.Contains("Stay", sourceContent);
    var targetContent = await File.ReadAllTextAsync(targetFile);
    Assert.Contains("class ToMove", targetContent);
}

[Fact]
public async Task InlineVariable_ReplacesUsages()
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"inline-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    var filePath = Path.Combine(tempDir, "Inline.cs");
    await File.WriteAllTextAsync(filePath, """
        namespace Test;
        public class Calc
        {
            public int Get()
            {
                var x = 42;
                return x;
            }
        }
        """);

    var tools = new IAW.Agents.CSharp.Roslyn.Tools.RefactoringTools(() => tempDir, null);
    var result = await tools.InlineVariableAsync(filePath, "x", 6);
    Assert.Contains("Inlined", result);
    var content = await File.ReadAllTextAsync(filePath);
    Assert.Contains("return 42;", content);
    Assert.DoesNotContain("var x", content);
}
```

- [ ] **Step 2: Implement MoveType**

1. Parse source file, find type declaration by name
2. Remove it from source tree
3. Create target file with namespace + the type + required usings
4. Format both files, write back

- [ ] **Step 3: Implement InlineVariable**

1. Parse file, find the variable declaration on the given line
2. Get the initializer expression
3. Find all references to the variable in the containing method (syntax-based search by identifier name)
4. Replace each reference with the initializer
5. Remove the variable declaration
6. Format and write back

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~RefactoringToolsTests" -v m`
Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/Roslyn/Tools/RefactoringTools.cs test/Core.Tests/RefactoringToolsTests.cs
git commit -m "feat: add MoveType and InlineVariable refactoring tools"
```

---

### Task 7: Register RefactoringTools and ImplementInterface

Wire RefactoringTools into RoslynAgent and add ImplementInterface (which needs semantic model).

**Files:**
- Modify: `src/Agents.CSharp/Roslyn/RoslynAgent.cs`
- Modify: `src/Agents.CSharp/Roslyn/IRoslyn.cs`

- [ ] **Step 1: Add ImplementInterface to IRoslyn**

```csharp
Task<string> ImplementInterfaceAsync(string filePath, string className, string interfaceName, CancellationToken ct = default);
```

- [ ] **Step 2: Register RefactoringTools in DefineTools**

```csharp
RegisterToolMethods(tools, new Tools.RefactoringTools(getWorkspace, _workspaceManager));
```

- [ ] **Step 3: Implement ImplementInterfaceAsync in RoslynAgent**

This needs the semantic model from the workspace to know what methods the interface declares:
1. Check workspace is ready, return "Workspace loading" if not
2. Find the class and interface via compilation
3. Determine which interface members are not yet implemented
4. Generate method stubs via SyntaxFactory
5. Add to class, format, write back

- [ ] **Step 4: Build and test**

Run: `dotnet build IAW.slnx && dotnet test test/Core.Tests --filter "FullyQualifiedName~Roslyn" -v m`
Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/Roslyn/RoslynAgent.cs src/Agents.CSharp/Roslyn/IRoslyn.cs
git commit -m "feat: register RefactoringTools, add ImplementInterface to RoslynAgent"
```

---

### Task 8: Full Build, Test, and Verification

**Files:** None (verification only)

- [ ] **Step 1: Build the solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All pass.

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve issues from code tools integration"
```
