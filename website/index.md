---
layout: home

hero:
  name: Interactive Agents
  text: Build intelligent agent systems on .NET
  tagline: An open-source ecosystem of agents that collaborate, remember, improve, and orchestrate tasks -- powered by Orleans and Aspire.
  image:
    src: /logo.svg
    alt: Interactive Agents
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: View on GitHub
      link: https://github.com/InteractiveAgents/IAW

---

<BehaviorTabs />

## Quick Start

### Install the package

```bash
dotnet add package IAW.Core
```

### Create your first agent

```csharp
using Core.V2;

public class Greeter : AgentV2
{
    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Greeter",
        Instructions = "You are a friendly greeter."
    };
}
```

### Configure with Aspire

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>();

builder.AddProject<Projects.MySilo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.Build().Run();
```
