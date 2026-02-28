# Step 1: Conversations + LLM — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement `IAgent` interface, `Agent` base class with `SendAsync` streaming, `[Llm<T>]` attribute, and model markers so that a `WeatherAgent` can chat via a wrapped `AIAgent`.

**Architecture:** `Agent` wraps Microsoft.Agents.AI's `AIAgent` internally. `[Llm<T>]` attribute on the class tells the framework which model to resolve. Framework property-injects the `AIAgent` at activation — no constructor parameters on Agent.

**Tech Stack:** .NET 11, Microsoft.Agents.AI (1.0.0-rc2), Microsoft.Extensions.AI.Abstractions

---

### Task 1: Add package references to Core.csproj

**Files:**
- Modify: `src/Core/Core.csproj`

**Step 1: Add Microsoft.Agents.AI package reference**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" />
  </ItemGroup>

</Project>
```

**Step 2: Verify it restores**

Run: `dotnet restore src/Core/Core.csproj`
Expected: Restore succeeded.

**Step 3: Commit**

```bash
git add src/Core/Core.csproj
git commit -m "feat(core): add Microsoft.Agents.AI package reference"
```

---

### Task 2: Create model markers

**Files:**
- Create: `src/Core/Models.cs`

**Step 1: Write model marker types**

These are empty sealed classes used as generic type arguments for `[Llm<T>]`.

```csharp
namespace Core;

public sealed class Claude45Haiku;
public sealed class Claude45Sonnet;
public sealed class Gpt53;
public sealed class OllamaLocal;
```

**Step 2: Verify it builds**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Core/Models.cs
git commit -m "feat(core): add LLM model marker types"
```

---

### Task 3: Create [Llm<T>] attribute

**Files:**
- Create: `src/Core/LlmAttribute.cs`

**Step 1: Write the attribute**

```csharp
namespace Core;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class LlmAttribute<TModel> : Attribute;
```

**Step 2: Verify it builds**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Core/LlmAttribute.cs
git commit -m "feat(core): add [Llm<T>] attribute for model declaration"
```

---

### Task 4: Write IAgent interface

**Files:**
- Modify: `src/Core/IAgent.cs` (replace contents)

**Step 1: Write the thin interface**

```csharp
namespace Core;

public interface IAgent
{
    string Id { get; }
    string DisplayName { get; }
    IAsyncEnumerable<string> SendAsync(string message, CancellationToken ct = default);
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Core/IAgent.cs
git commit -m "feat(core): define thin IAgent interface"
```

---

### Task 5: Write Agent base class

**Files:**
- Create: `src/Core/Agent.cs`

**Step 1: Write the base class**

`Agent` wraps `AIAgent` via property injection. `SendAsync` delegates to `AIAgent.RunStreamingAsync`, yielding `AgentResponseUpdate.Text` for each streaming chunk.

```csharp
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;

namespace Core;

public class Agent : IAgent
{
    public string Id => GetType().Name;
    public virtual string DisplayName => Id;

    protected AIAgent? Llm { get; internal set; }

    public virtual async IAsyncEnumerable<string> SendAsync(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Llm is null)
            yield break;

        await foreach (var update in Llm.RunStreamingAsync(message, cancellationToken: ct))
        {
            if (update.Text is { Length: > 0 } text)
                yield return text;
        }
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Core/Agent.cs
git commit -m "feat(core): implement Agent base class wrapping AIAgent"
```

---

### Task 6: Write WeatherAgent sample

**Files:**
- Create: `src/Core/WeatherAgent.cs`

**Step 1: Write the sample agent**

```csharp
namespace Core;

[Llm<OllamaLocal>]
public class WeatherAgent : Agent
{
    public override string DisplayName => "Weather";
}
```

**Step 2: Remove old IAgent.cs content that had WeatherAgent and Agent stubs**

The old `IAgent.cs` had `Agent` class and `WeatherAgent` class inline. Those are now in separate files. Ensure `IAgent.cs` only has the interface.

**Step 3: Verify full build**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded. No warnings about duplicate type definitions.

**Step 4: Commit**

```bash
git add src/Core/WeatherAgent.cs src/Core/IAgent.cs
git commit -m "feat(core): add WeatherAgent sample using [Llm<T>]"
```

---

### Task 7: Verify solution builds end-to-end

**Step 1: Build the entire solution**

Run: `dotnet build` (from repo root E:\IAW\InteractiveAgents\IAW)
Expected: Build succeeded for all projects.

**Step 2: If build fails, fix any reference issues in dependent projects**

Projects that reference Core may need updates if they used the old empty `IAgent`/`Agent`.

**Step 3: Final commit if any fixes were needed**

```bash
git add -A
git commit -m "fix: resolve build issues after IAgent refactor"
```
