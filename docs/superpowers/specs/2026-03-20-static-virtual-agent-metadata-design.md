# Static Virtual Interface Members for Agent Metadata

**Date:** 2026-03-20
**Status:** Draft

## Problem

Agent identity is scattered across two places:
- **Interface** (`IAspire : IAgent`) — empty marker, only declares the grain contract
- **Class** (`AspireAgent`) — defines `DisplayName`, `Instructions`, `AgentDescription`, `AgentCapabilities` via a mix of virtual overrides and static properties

This means you can't understand what an agent is by reading its interface. The registry uses reflection hacks (`BindingFlags.Static | BindingFlags.FlattenHierarchy`) to read static properties from classes. There's no compile-time enforcement that agents provide metadata.

## Design

Move all agent identity to the interface using C# static virtual interface members. Add a generic `Agent<TContract>` base class that bridges static interface metadata to instance properties via the type parameter.

### IAgent — Static Virtual Declarations

```csharp
public interface IAgent : IGrainWithStringKey
{
    static virtual string AgentDisplayName => "";
    static virtual string AgentDescription => "";
    static virtual string[] AgentCapabilities => [];
    static virtual string AgentInstructions =>
        "You are a helpful AI assistant. Answer questions clearly and concisely.";

    // ... existing grain methods unchanged
}
```

### Derived Interfaces — Self-Describing Contracts

```csharp
public interface IAspire : IAgent
{
    static new string AgentDisplayName => "Aspire";

    static new string AgentDescription =>
        "Monitors and manages the running .NET Aspire application — resources, health, logs, traces, and telemetry via Aspire MCP tools.";

    static new string[] AgentCapabilities =>
        ["aspire", "health", "traces", "logs", "resources", "monitoring", "telemetry", "infrastructure", "status"];

    static new string AgentInstructions => """
        You are the Aspire infrastructure agent for the IAW system. You monitor and manage
        the running .NET Aspire application — its resources, health, logs, and traces.
        ...
        """;
}
```

### Agent<TContract> — Generic Bridge

```csharp
public abstract class Agent<TContract>(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient) where TContract : IAgent
{
    protected override string DisplayName => TContract.AgentDisplayName;
    protected override string Instructions => TContract.AgentInstructions;
}
```

The non-generic `Agent` base class is unchanged. `DisplayName` and `Instructions` remain `protected virtual string` with defaults. `Agent<TContract>` simply overrides them to read from the interface's static virtuals.

### Agent Classes — Minimal

```csharp
public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    ILogger<AspireAgent> logger)
    : Agent<IAspire>(durableState, chatClient), IAspire
{
    // No DisplayName, Instructions, AgentDescription, AgentCapabilities
    // Only behavior: MCP connection, tools, activation logic
}
```

### Dynamic Instructions Override

`CodeOrchestratorAgent` builds instructions dynamically (injects agent catalog at activation). It simply overrides `Instructions` as today — the generic base's default is ignored:

```csharp
public class CodeOrchestratorAgent(...)
    : Agent<ICodeOrchestrator>(...), ICodeOrchestrator
{
    // ICodeOrchestrator.AgentInstructions provides the static default
    // but this class overrides for dynamic behavior
    protected override string Instructions => _cachedInstructions.Length > 0
        ? _cachedInstructions : BuildFallbackInstructions();
}
```

No branching logic. Standard virtual dispatch — class override wins when present.

### AgentRegistrationStartupTask — No More Reflection Hacks

Replace reflection-based property reading with a generic helper:

```csharp
static class AgentInterfaceMetadata
{
    public static string DisplayName<T>() where T : IAgent => T.AgentDisplayName;
    public static string Description<T>() where T : IAgent => T.AgentDescription;
    public static string[] Capabilities<T>() where T : IAgent => T.AgentCapabilities;
    public static string Instructions<T>() where T : IAgent => T.AgentInstructions;
}
```

The registration task already discovers the interface type. It invokes the generic helper via `MakeGenericMethod` once per agent at startup:

```csharp
static AgentRecord? BuildRecord(Type agentType)
{
    var agentInterface = agentType.GetInterfaces()
        .FirstOrDefault(i => i != typeof(IAgent)
            && typeof(IAgent).IsAssignableFrom(i) && !i.IsGenericType);

    if (agentInterface is null) return null;

    var descMethod = typeof(AgentInterfaceMetadata)
        .GetMethod(nameof(AgentInterfaceMetadata.Description))!
        .MakeGenericMethod(agentInterface);
    var capsMethod = typeof(AgentInterfaceMetadata)
        .GetMethod(nameof(AgentInterfaceMetadata.Capabilities))!
        .MakeGenericMethod(agentInterface);

    var description = (string)descMethod.Invoke(null, null)!;
    var capabilities = (string[])capsMethod.Invoke(null, null)!;
    var displayName = /* same pattern for DisplayName */;

    return new AgentRecord { ... };
}
```

Still one `MakeGenericMethod` call per agent at startup, but the actual dispatch is compile-time safe inside the generic method. No `BindingFlags`, no silent empty-string fallbacks.

### GetMetadata() — Reads from Existing Virtuals

`Agent.Lifecycle.cs` `GetMetadata()` already reads `DisplayName` and `Instructions` as virtual properties. Since `Agent<TContract>` overrides them to read from the interface, `GetMetadata()` automatically picks up the interface values. No changes needed.

## Files Changed

### Core (src/Core)
| File | Change |
|------|--------|
| `Contracts/IAgent.cs` | Add 4 static virtual members |
| `Agents/Agent.cs` | No change (DisplayName/Instructions remain virtual) |
| `Agents/Agent.Lifecycle.cs` | No change |
| `Agents/AgentGeneric.cs` | **New** — `Agent<TContract>` (~10 lines) |
| `Registry/AgentInterfaceMetadata.cs` | **New** — generic helper (~15 lines) |
| `Registry/AgentRegistrationStartupTask.cs` | Replace reflection with generic helper |

### Agent Interfaces (~29 files)
Every `IFoo : IAgent` interface gets the 4 static members moved from its corresponding agent class.

### Agent Classes (~25 files)
Every agent class:
- Changes base from `Agent` to `Agent<IFoo>`
- Removes `DisplayName`, `Instructions`, `AgentDescription`, `AgentCapabilities`
- Exception: `CodeOrchestratorAgent` keeps `Instructions` override

## Runtime

Target is .NET 11 preview (C# preview). Static virtual interface members have been stable since .NET 7/C# 11 — four major versions of runtime support. No compatibility concerns.

## Risks

1. **Orleans grain type resolution** — Adding `Agent<TContract>` to the inheritance chain. Since it's abstract and never instantiated directly, Orleans only sees concrete types. The existing `[GrainType]` on `Agent` or concrete classes is unaffected.

2. **Large diff** — ~35 files changed. Mechanical and easily reviewable, but merge conflicts likely if other branches touch agent files.

## Non-Goals

- Changing how `GetCapabilities()` works (runtime reflection for P2P/events/tools — orthogonal)
- Changing Orleans grain IDs or types
- Modifying the `AgentRecord` schema
