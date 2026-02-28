# IAW Open-Source Website & Project Polish Design

## Goal

Make IAW look and feel like a well-maintained open-source project: professional documentation website (VitePress), proper README, CI/CD, contribution guidelines, and GitHub community files.

## Architecture

Two deliverables in one repo:

1. **VitePress documentation site** in `website/` folder, deployed to GitHub Pages at `https://interactiveagents.github.io/IAW`
2. **OSS polish** — README, CI/CD workflows, contribution guides, issue templates, editor config, NuGet metadata

## Tech Stack

- VitePress (static site generator, Vue-based)
- GitHub Actions (CI/CD + GitHub Pages deployment)
- .NET 11.0, Orleans 10.0, Aspire 13.1 (existing stack)

## Brand (from existing Figma design)

- Primary: Indigo `#6366f1`
- Static agents: Emerald `#10b981`
- Dynamic agents: Amber `#f59e0b`
- Interactive features: Purple `#8b5cf6`
- Tagline: "An open-source ecosystem of intelligent agents that collaborate, remember, improve, and orchestrate tasks — without reinventing the wheel."

---

## Part 1: VitePress Documentation Site

### Location

`website/` folder in the IAW repo root.

### Site Map

| Route | Page | Content |
|-------|------|---------|
| `/` | Landing | Hero with tagline, features grid, code example, CTA buttons |
| `/guide/` | Getting Started | Install NuGet, configure Aspire, create first agent |
| `/guide/architecture` | Architecture | Orleans grains, DurableGrain, behavior interfaces, durable state |
| `/guide/agents` | Building Agents | Agent base class, DefineTools, SendAsync, SystemPrompt |
| `/guide/notifications` | Notifications & Events | Pub/sub, NotificationEnvelope, event streaming |
| `/guide/telegram` | Telegram Bot | TelegramBotGrain integration walkthrough |
| `/guide/testing` | Testing | TestCluster unit tests, Aspire integration tests |
| `/reference/` | API Reference | IAgent, behavior interfaces, contract types, Agent class |

### Folder Structure

```
website/
├── .vitepress/
│   └── config.mts
├── public/
│   └── logo.svg
├── index.md
├── guide/
│   ├── index.md
│   ├── architecture.md
│   ├── agents.md
│   ├── notifications.md
│   ├── telegram.md
│   └── testing.md
├── reference/
│   └── index.md
└── package.json
```

### VitePress Config

- Navigation: Guide | Reference | GitHub
- Sidebar: Getting Started > Architecture > Agents > Notifications > Telegram > Testing
- Theme: Default with indigo brand color override
- Dark mode: enabled (default)
- Search: VitePress built-in local search
- Social links: GitHub repo
- Footer: "Released under MIT License"

### Landing Page

Hero section with:
- Title: "Interactive Agents"
- Tagline from Figma design
- Two CTA buttons: "Get Started" → /guide/, "View on GitHub" → repo
- Features grid: 6 key capabilities (Memory, P2P, Events, Tools, Timers, Observability)
- Code example: minimal Agent subclass

---

## Part 2: IAW Repo OSS Polish

### Root Files

| File | Content |
|------|---------|
| `README.md` | Badges (CI, license, .NET), project description, features list, quickstart code block, architecture overview, package table, links to docs |
| `CONTRIBUTING.md` | Fork/PR workflow, coding standards (no summary comments, self-explanatory naming), test requirements (unit + integration), commit message format |
| `CODE_OF_CONDUCT.md` | Contributor Covenant v2.1 |
| `CHANGELOG.md` | Initial entry documenting current state |
| `.editorconfig` | C# style rules (from existing parent directory config) |
| `global.json` | Pin SDK to 11.0.100-preview.1 |

### GitHub Community Files

| File | Content |
|------|---------|
| `.github/workflows/ci.yml` | On PR + push to main: restore, build IAW.slnx, run all tests (unit + integration) on Windows |
| `.github/workflows/docs.yml` | On push to main (website/** changed): install Node, build VitePress, deploy to GitHub Pages |
| `.github/ISSUE_TEMPLATE/bug_report.yml` | YAML form: description, steps to reproduce, expected behavior, .NET version |
| `.github/ISSUE_TEMPLATE/feature_request.yml` | YAML form: description, use case, proposed solution |
| `.github/PULL_REQUEST_TEMPLATE.md` | Checklist: description, tests added, docs updated, breaking changes |
| `.github/dependabot.yml` | Weekly NuGet + npm (website/) dependency checks |

### NuGet Package Metadata

Add to `Directory.Build.props`:
- `<Authors>InteractiveAgents</Authors>`
- `<PackageLicenseExpression>MIT</PackageLicenseExpression>`
- `<RepositoryUrl>https://github.com/InteractiveAgents/IAW</RepositoryUrl>`
- `<PackageProjectUrl>https://interactiveagents.github.io/IAW</PackageProjectUrl>`
- SourceLink configuration for debuggable packages

### README Structure

1. Logo/title
2. Badges row (CI status, MIT license, .NET 11)
3. One-paragraph description
4. Features bullet list (6-8 items)
5. Quick Start code block (3-step: install, configure, create agent)
6. Package table (Core, Hosting, Testing, etc.)
7. Documentation link
8. Contributing link
9. License

---

## Decisions Made

- Website uses VitePress (not DocFX or Docusaurus) — proven by Wolverine/Marten for .NET OSS
- Website lives in `website/` folder in IAW repo (not separate repo)
- Deploys to `https://interactiveagents.github.io/IAW` (project-level GitHub Pages)
- Brand colors extracted from existing Figma design
- No SECURITY.md (user decision)
- Full OSS polish including CI/CD, contribution guides, templates
- NuGet metadata included in Directory.Build.props
