# Contributing to IAW

We welcome contributions! Here's how to get started.

## Development Setup

1. Install [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
2. Clone the repository:
   ```bash
   git clone https://github.com/InteractiveAgents/IAW.git
   cd IAW
   ```
3. Build:
   ```bash
   dotnet build IAW.slnx
   ```
4. Run tests:
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
- Use self-explanatory C# naming — no `/// <summary>` comments unless they add real value
- Only add inline comments in exceptional cases where logic isn't self-evident
- Use `var` sparingly — prefer explicit types

## Testing

- Unit tests use xUnit v3 with Orleans `TestClusterBuilder`
- Integration tests use Aspire `DistributedApplicationTestingBuilder`
- Add tests for new features and bug fixes
- Run the full suite before submitting PRs:
  ```bash
  dotnet test IAW.slnx
  ```

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
