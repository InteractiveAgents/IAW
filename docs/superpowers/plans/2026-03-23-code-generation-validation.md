# Code Generation Validation & Sanitization Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the "Code generation failed after 3 attempts" errors by adding code validation, auto-sanitization, and enriched error feedback to the CodeOrchestratorAgent.

**Architecture:** Add a `CodeValidator` in `Core.Orchestration` that validates/sanitizes generated code before `dotnet build` — fixing invalid namespaces, partial qualifiers, and missing boilerplate. Enrich the system prompt and regeneration feedback with the actual list of available interfaces and namespaces so the LLM stops hallucinating.

**Tech Stack:** C# 13 / .NET 11, Orleans, string-based validation (no Roslyn dependency in Core)

---

### Task 1: Create CodeValidator in Core.Orchestration

**Files:**
- Create: `src/Core/Orchestration/CodeValidator.cs`

- [ ] **Step 1: Write the failing test**

Create test file:

```csharp
// test/Core.Tests/Orchestration/CodeValidatorTests.cs
using Core.Orchestration;

namespace Core.Tests.Orchestration;

public class CodeValidatorTests
{
    [Fact]
    public void Sanitize_RemovesInvalidUsings()
    {
        var code = """
            using System.Text.Json;
            using IAW.Agents.LLM;
            using IAW.Agents.System;

            await using var iaw = await IAWCluster.Connect(args);
            """;

        var result = CodeValidator.Sanitize(code);

        Assert.DoesNotContain("using IAW.Agents.LLM;", result.Code);
        Assert.Contains("using IAW.Agents.System;", result.Code);
        Assert.Contains("IAW.Agents.LLM", result.RemovedUsings);
    }

    [Fact]
    public void Sanitize_FixesPartialQualifiers()
    {
        var code = """
            using IAW.Agents.Models;

            var gpt = iaw.Get<Models.IGpt4o>(taskId);
            """;

        var result = CodeValidator.Sanitize(code);

        Assert.DoesNotContain("Models.IGpt4o", result.Code);
    }

    [Fact]
    public void Validate_DetectsMissingBoilerplate()
    {
        var code = """
            using System;
            Console.WriteLine("no boilerplate");
            """;

        var result = CodeValidator.Validate(code);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Contains("IAWCluster.Connect"));
    }

    [Fact]
    public void Validate_AcceptsCorrectCode()
    {
        var code = """
            using System.Text.Json;
            using Aspire.IAW;
            using Core;
            using Core.Contracts;
            using IAW.Agents.System;

            await using var iaw = await IAWCluster.Connect(args);
            var taskId = iaw.TaskId;
            var shell = iaw.Get<IShell>(taskId);
            """;

        var result = CodeValidator.Validate(code);

        Assert.True(result.IsValid);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CodeValidatorTests" -v minimal`
Expected: FAIL — `CodeValidator` does not exist yet

- [ ] **Step 3: Implement CodeValidator**

```csharp
// src/Core/Orchestration/CodeValidator.cs
namespace Core.Orchestration;

public static class CodeValidator
{
    static readonly HashSet<string> ValidNamespaces =
    [
        "System", "System.Text.Json", "System.Text", "System.IO",
        "System.Linq", "System.Collections.Generic", "System.Threading.Tasks",
        "Aspire.IAW", "Core", "Core.Contracts",
        "IAW.Agents.System", "IAW.Agents.Coding", "IAW.Agents.Infrastructure",
        "IAW.Agents.Memory", "IAW.Agents.Orchestration", "IAW.Agents.Models",
        "IAW.Agents.Messages"
    ];

    // Partial qualifiers that LLMs use incorrectly — map to empty (strip qualifier, keep type)
    static readonly Dictionary<string, string> QualifierFixes = new(StringComparer.Ordinal)
    {
        ["Models."] = "",
        ["System."] = "System.",  // keep System. — it's valid
        ["Coding."] = "",
        ["Infrastructure."] = "",
        ["Memory."] = "",
    };

    // Known invalid namespace patterns
    static readonly string[] InvalidNamespacePatterns =
    [
        "IAW.Agents.LLM",
        "IAW.Agents.AI",
        "IAW.Agents.Tools",
        "IAW.Agents.Core",
        "IAW.Agents.Contracts",
        "IAW.Agents.Services",
    ];

    public static SanitizeResult Sanitize(string code)
    {
        var removedUsings = new List<string>();
        var fixes = new List<string>();
        var lines = code.Split('\n').ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            // Fix invalid using directives
            if (trimmed.StartsWith("using ") && trimmed.EndsWith(';') && !trimmed.Contains('('))
            {
                var ns = trimmed["using ".Length..^1].Trim();
                if (IsInvalidNamespace(ns))
                {
                    removedUsings.Add(ns);
                    lines[i] = ""; // remove the line
                    continue;
                }
            }

            // Fix partial qualifiers in iaw.Get<T> calls: iaw.Get<Models.IGpt4o> → iaw.Get<IGpt4o>
            if (lines[i].Contains("iaw.Get<"))
            {
                var original = lines[i];
                foreach (var (qualifier, replacement) in QualifierFixes)
                {
                    if (qualifier == "System.") continue; // don't touch System.
                    var pattern = $"iaw.Get<{qualifier}";
                    if (lines[i].Contains(pattern))
                        lines[i] = lines[i].Replace(pattern, $"iaw.Get<{replacement}");
                }
                if (lines[i] != original)
                    fixes.Add($"Fixed qualifier: {original.Trim()} → {lines[i].Trim()}");
            }
        }

        // Remove empty lines left by stripped usings (but keep one blank)
        var result = string.Join('\n', lines);
        while (result.Contains("\n\n\n"))
            result = result.Replace("\n\n\n", "\n\n");

        return new SanitizeResult(result, removedUsings, fixes);
    }

    public static ValidationResult Validate(string code)
    {
        var issues = new List<string>();

        if (!code.Contains("IAWCluster.Connect"))
            issues.Add("Missing required boilerplate: await using var iaw = await IAWCluster.Connect(args);");

        if (!code.Contains("iaw.TaskId") && !code.Contains("taskId"))
            issues.Add("Missing taskId: var taskId = iaw.TaskId;");

        if (!code.Contains("result.json"))
            issues.Add("Missing result.json output — generated code must write result.json");

        return new ValidationResult(issues.Count == 0, issues);
    }

    static bool IsInvalidNamespace(string ns)
    {
        // Check known invalid patterns
        foreach (var pattern in InvalidNamespacePatterns)
            if (ns.Equals(pattern, StringComparison.Ordinal))
                return true;

        // Check if it's a using for a namespace we don't recognize at all
        if (ns.StartsWith("IAW.") && !ValidNamespaces.Any(v => ns.StartsWith(v)))
            return true;

        return false;
    }

    public static string GetAvailableTypesHint() => """
        VALID NAMESPACES AND INTERFACES (use ONLY these):
          IAW.Agents.System      → IShell, IFileSystem
          IAW.Agents.Coding      → IGit, IRoslyn, IDotNet, INuGet, IGitHub
          IAW.Agents.Infrastructure → IAspire
          IAW.Agents.Memory      → ICodeMemory, IUserMemory, IProjectMemory, IEpisodeMemory, IPatternMemory, IKnowledge
          IAW.Agents.Orchestration → IThread
          IAW.Agents.Models      → (no interfaces — LLM agents have no public interfaces, do NOT use them)
          Core.Contracts         → IAgent, ICodeOrchestrator, IMemoryAgent

        INVALID (do NOT use): IAW.Agents.LLM, IAW.Agents.AI, IAW.Agents.Tools, Models.IXxx qualifiers
        Use interface names directly after importing the namespace: iaw.Get<IShell>(taskId), NOT iaw.Get<System.IShell>(taskId)
        """;
}

public record SanitizeResult(string Code, List<string> RemovedUsings, List<string> Fixes);
public record ValidationResult(bool IsValid, List<string> Issues);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CodeValidatorTests" -v minimal`
Expected: ALL PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Orchestration/CodeValidator.cs test/Core.Tests/Orchestration/CodeValidatorTests.cs
git commit -m "feat: add CodeValidator for orchestration code sanitization"
```

---

### Task 2: Integrate CodeValidator into CodeOrchestratorAgent

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`

- [ ] **Step 1: Add sanitization after code generation (line ~208-210)**

After `GenerateCode` returns, add sanitization before writing to file:

```csharp
// After line 208: var code = await GenerateCode(prompt, ct);
// Add:
var sanitized = CodeValidator.Sanitize(code);
if (sanitized.RemovedUsings.Count > 0 || sanitized.Fixes.Count > 0)
    await PublishProgress(projectKey, taskId, "sanitizing",
        $"Auto-fixed: removed {sanitized.RemovedUsings.Count} invalid usings, applied {sanitized.Fixes.Count} fixes", ct);
code = sanitized.Code;

// Add validation check
var validation = CodeValidator.Validate(code);
if (!validation.IsValid)
    await PublishProgress(projectKey, taskId, "warning",
        $"Validation warnings: {string.Join("; ", validation.Issues)}", ct);
```

- [ ] **Step 2: Add sanitization after regeneration (line ~227)**

After `RegenerateCode` returns, sanitize again:

```csharp
// After line 227: code = await RegenerateCode(prompt, code, buildErrors, ct);
// Add:
var reSanitized = CodeValidator.Sanitize(code);
code = reSanitized.Code;
```

- [ ] **Step 3: Enrich error feedback in RegenerateCode method**

Modify `RegenerateCode` (line 305-334) to include available types hint:

```csharp
// In the user message at line 317-318, change to:
new(Microsoft.Extensions.AI.ChatRole.User,
    $"""
    The code above has build errors. Fix them and output the COMPLETE corrected code.

    Build errors:
    {buildErrors}

    {CodeValidator.GetAvailableTypesHint()}
    """)
```

- [ ] **Step 4: Add available types to system prompt**

In `BuildInstructions` method, add the types hint before the RULES section (around line 157):

```csharp
// Before the RULES: section, insert:
{CodeValidator.GetAvailableTypesHint()}
```

- [ ] **Step 5: Build and verify compilation**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs
git commit -m "feat: integrate CodeValidator into orchestration pipeline"
```

---

### Task 3: Build, Run Aspire, and Test via MCP

- [ ] **Step 1: Build the full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx -v minimal`
Expected: All tests pass

- [ ] **Step 3: Start Aspire and verify**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Wait for all resources to come up healthy.

- [ ] **Step 4: Test via IAW MCP**

Send a message through the IAW MCP that will trigger code orchestration (e.g., "What's the system status?") and verify it no longer fails with the namespace errors.

- [ ] **Step 5: Check traces for success**

Use Aspire MCP to check traces and logs — confirm the orchestration completes without "Code generation failed" errors.

- [ ] **Step 6: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix: resolve code generation validation issues"
```
