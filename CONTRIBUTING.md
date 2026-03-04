# Contributing to IAW

We welcome contributions! Here's how to get started.

## Development Setup

1. Install [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
2. Install [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
3. Clone the repository:
   ```bash
   git clone https://github.com/InteractiveAgents/IAW.git
   cd IAW
   ```
4. Build:
   ```bash
   dotnet build IAW.slnx
   ```
5. Run tests:
   ```bash
   dotnet test IAW.slnx
   ```
6. Run locally:
   ```bash
   aspire run
   ```

## Writing Agents

Agents extend `AgentV2` and override `Profile` and `OnRespondAsync`:

```csharp
using Core.V2;

public class MyAgent : AgentV2
{
    protected override AgentProfile Profile => new()
    {
        DisplayName = "My Agent",
        Instructions = "You handle specific tasks.",
        Capabilities = ["chat", "tools"]
    };

    protected override async Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default)
    {
        // Your logic here
        return new AgentReply { Output = "Done." };
    }
}
```

## Testing Agents

Use `AgentTestV2<T>` to get 16 universal behavior tests for free:

```csharp
public class MyAgentTests : AgentTestV2<MyAgent>
{
    [Fact]
    public async Task CustomBehavior()
    {
        // Agent-specific test logic
    }
}
```

Run the full suite before submitting PRs:
```bash
dotnet test IAW.slnx
```

## Making Changes

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Ensure all tests pass: `dotnet test IAW.slnx`
5. Commit with a descriptive message
6. Push and open a Pull Request

## Code Style

- Follow the `.editorconfig` rules (enforced automatically by IDEs)
- Use self-explanatory C# naming -- no `/// <summary>` comments unless they add real value
- Only add inline comments in exceptional cases where logic isn't self-evident
- All serializable Orleans types need `[GenerateSerializer]` and `[Id(n)]` attributes

## Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):
- `feat:` new feature
- `fix:` bug fix
- `refactor:` code change that neither fixes a bug nor adds a feature
- `docs:` documentation only
- `test:` adding or updating tests
- `chore:` maintenance tasks

## Questions?

Open an issue or start a discussion on GitHub.
