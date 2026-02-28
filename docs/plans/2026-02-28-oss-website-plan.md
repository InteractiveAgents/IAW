# IAW Open-Source Website & Project Polish Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make IAW look and feel like a well-maintained open-source project with a VitePress documentation website, proper README, CI/CD, and community files.

**Architecture:** Two deliverables in one repo: (1) VitePress documentation site in `website/` folder deployed to GitHub Pages, (2) OSS polish files (README, CI/CD, contributing guide, issue templates, editor config, NuGet metadata). Brand colors and copy extracted from existing Figma design.

**Tech Stack:** VitePress 2.x, GitHub Actions, .NET 11.0, Node.js 24

---

### Task 1: Add root-level OSS files (.editorconfig, global.json, .gitattributes)

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/.editorconfig`
- Create: `E:/IAW/InteractiveAgents/IAW/global.json`
- Create: `E:/IAW/InteractiveAgents/IAW/.gitattributes`

**Step 1: Copy .editorconfig from parent directory**

Copy `E:/IAW/.editorconfig` into the repo root at `E:/IAW/InteractiveAgents/IAW/.editorconfig`. The file is 247 lines of C# code style rules. Copy it exactly as-is — it already has `root = true`, 4-space indents, CRLF line endings, PascalCase naming, and all the C# conventions the project uses.

**Step 2: Create global.json**

```json
{
  "sdk": {
    "version": "11.0.100-preview.1.26104.118",
    "rollForward": "latestPatch"
  }
}
```

This pins the .NET SDK version so all contributors use the same version.

**Step 3: Create .gitattributes**

```
* text=auto
*.cs text diff=csharp
*.csproj text
*.slnx text
*.md text
*.json text
*.xml text
*.yml text
*.yaml text
```

**Step 4: Build to verify nothing broke**

Run: `dotnet build E:/IAW/InteractiveAgents/IAW/IAW.slnx`
Expected: 0 warnings, 0 errors

**Step 5: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add .editorconfig global.json .gitattributes
git commit -m "chore: add .editorconfig, global.json, .gitattributes"
```

---

### Task 2: Create Directory.Build.props with NuGet metadata

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/Directory.Build.props`

**Step 1: Create Directory.Build.props**

This file defines shared build properties AND NuGet package metadata for all projects in the repo. The parent directory `E:/IAW/` has a `Directory.Build.props` with target framework settings, but the repo itself needs its own.

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
  </PropertyGroup>

  <PropertyGroup>
    <Authors>InteractiveAgents</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/InteractiveAgents/IAW</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageProjectUrl>https://interactiveagents.github.io/IAW</PackageProjectUrl>
    <Copyright>Copyright (c) 2026 InteractiveAgents</Copyright>
  </PropertyGroup>
</Project>
```

**Step 2: Verify the parent Directory.Build.props won't conflict**

The parent `E:/IAW/Directory.Build.props` sets the same TargetFramework and LangVersion. MSBuild walks up directories and applies all `Directory.Build.props` files. Since both set the same values, there's no conflict. If issues arise, the repo-level file takes precedence for projects inside it.

**Step 3: Build to verify**

Run: `dotnet build E:/IAW/InteractiveAgents/IAW/IAW.slnx`
Expected: 0 warnings, 0 errors

Check that individual projects still compile correctly — the new `Directory.Build.props` should not break anything since it matches what the parent already provides plus adds NuGet metadata.

**Step 4: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add Directory.Build.props
git commit -m "chore: add Directory.Build.props with NuGet metadata"
```

---

### Task 3: Create community files (CONTRIBUTING, CODE_OF_CONDUCT, CHANGELOG)

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/CONTRIBUTING.md`
- Create: `E:/IAW/InteractiveAgents/IAW/CODE_OF_CONDUCT.md`
- Create: `E:/IAW/InteractiveAgents/IAW/CHANGELOG.md`

**Step 1: Create CONTRIBUTING.md**

```markdown
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
```

**Step 2: Create CODE_OF_CONDUCT.md**

Use the Contributor Covenant v2.1 (industry standard). The full text is at https://www.contributor-covenant.org/version/2/1/code_of_conduct/. Create the file with:

```markdown
# Contributor Covenant Code of Conduct

## Our Pledge

We as members, contributors, and leaders pledge to make participation in our
community a harassment-free experience for everyone, regardless of age, body
size, visible or invisible disability, ethnicity, sex characteristics, gender
identity and expression, level of experience, education, socio-economic status,
nationality, personal appearance, race, caste, color, religion, or sexual
identity and orientation.

We pledge to act and interact in ways that contribute to an open, welcoming,
diverse, inclusive, and healthy community.

## Our Standards

Examples of behavior that contributes to a positive environment for our
community include:

* Demonstrating empathy and kindness toward other people
* Being respectful of differing opinions, viewpoints, and experiences
* Giving and gracefully accepting constructive feedback
* Accepting responsibility and apologizing to those affected by our mistakes,
  and learning from the experience
* Focusing on what is best not just for us as individuals, but for the overall
  community

Examples of unacceptable behavior include:

* The use of sexualized language or imagery, and sexual attention or advances of
  any kind
* Trolling, insulting or derogatory comments, and personal or political attacks
* Public or private harassment
* Publishing others' private information, such as a physical or email address,
  without their explicit permission
* Other conduct which could reasonably be considered inappropriate in a
  professional setting

## Enforcement

Instances of abusive, harassing, or otherwise unacceptable behavior may be
reported to the project maintainers. All complaints will be reviewed and
investigated promptly and fairly.

## Attribution

This Code of Conduct is adapted from the [Contributor Covenant](https://www.contributor-covenant.org),
version 2.1, available at
[https://www.contributor-covenant.org/version/2/1/code_of_conduct.html](https://www.contributor-covenant.org/version/2/1/code_of_conduct.html).
```

**Step 3: Create CHANGELOG.md**

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- Unified `Agent` base class merging `OrleansAgentGrain` and internal `Agent` into a single `DurableGrain`-based public class
- Generic tools API (`DefineTools()` + `InvokeToolAsync`) replacing hardcoded tool methods
- LLM integration via `Microsoft.Extensions.AI` (`SendAsync` returning `IAsyncEnumerable<string>`)
- 8 behavior interfaces: Metadata, State, History, Events, Notifications, Tracking, Tools, Streams
- Telegram Bot client with webhook support and forum topic management
- VitePress documentation website
- GitHub Actions CI/CD pipeline
- Observability via OpenTelemetry (ActivitySource, counters)

### Removed
- `OrleansAgentGrain` (merged into `Agent`)
- `IAgentConfigurationBehavior` (over-engineered, dropped)
- `SendDeterministicAsync` (placeholder, replaced by real LLM `SendAsync`)
- All `OrleansAgent*` prefixed type names (renamed to clean names)
```

**Step 4: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add CONTRIBUTING.md CODE_OF_CONDUCT.md CHANGELOG.md
git commit -m "docs: add CONTRIBUTING, CODE_OF_CONDUCT, CHANGELOG"
```

---

### Task 4: Create GitHub issue/PR templates and dependabot config

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/.github/ISSUE_TEMPLATE/bug_report.yml`
- Create: `E:/IAW/InteractiveAgents/IAW/.github/ISSUE_TEMPLATE/feature_request.yml`
- Create: `E:/IAW/InteractiveAgents/IAW/.github/PULL_REQUEST_TEMPLATE.md`
- Create: `E:/IAW/InteractiveAgents/IAW/.github/dependabot.yml`

**Step 1: Create bug report template**

```yaml
name: Bug Report
description: Report a bug in IAW
labels: ["bug"]
body:
  - type: textarea
    id: description
    attributes:
      label: Description
      description: A clear description of the bug
    validations:
      required: true
  - type: textarea
    id: steps
    attributes:
      label: Steps to Reproduce
      description: Steps to reproduce the behavior
      placeholder: |
        1. Create an agent with...
        2. Call method...
        3. See error...
    validations:
      required: true
  - type: textarea
    id: expected
    attributes:
      label: Expected Behavior
      description: What you expected to happen
    validations:
      required: true
  - type: textarea
    id: actual
    attributes:
      label: Actual Behavior
      description: What actually happened
    validations:
      required: true
  - type: input
    id: dotnet-version
    attributes:
      label: .NET Version
      description: Output of `dotnet --version`
      placeholder: "11.0.100-preview.1"
    validations:
      required: true
  - type: input
    id: os
    attributes:
      label: Operating System
      placeholder: "Windows 11, Ubuntu 24.04, macOS 15"
    validations:
      required: true
```

**Step 2: Create feature request template**

```yaml
name: Feature Request
description: Suggest a new feature for IAW
labels: ["enhancement"]
body:
  - type: textarea
    id: description
    attributes:
      label: Description
      description: A clear description of the feature you'd like
    validations:
      required: true
  - type: textarea
    id: use-case
    attributes:
      label: Use Case
      description: Why do you need this feature? What problem does it solve?
    validations:
      required: true
  - type: textarea
    id: proposed-solution
    attributes:
      label: Proposed Solution
      description: How would you like this to work?
    validations:
      required: false
  - type: textarea
    id: alternatives
    attributes:
      label: Alternatives Considered
      description: Any alternative solutions or features you've considered
    validations:
      required: false
```

**Step 3: Create PR template**

```markdown
## Description

<!-- What does this PR do? -->

## Changes

<!-- List the key changes -->

-

## Checklist

- [ ] Tests added/updated
- [ ] Documentation updated (if applicable)
- [ ] No breaking changes (or documented in description)
- [ ] `dotnet build IAW.slnx` passes with 0 warnings
- [ ] `dotnet test IAW.slnx` passes
```

**Step 4: Create dependabot.yml**

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
    open-pull-requests-limit: 5

  - package-ecosystem: npm
    directory: /website
    schedule:
      interval: weekly
    open-pull-requests-limit: 3

  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: weekly
    open-pull-requests-limit: 3
```

**Step 5: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add .github/
git commit -m "chore: add GitHub issue/PR templates and dependabot config"
```

---

### Task 5: Create GitHub Actions CI workflow

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/.github/workflows/ci.yml`

**Step 1: Create CI workflow**

This workflow builds and tests the entire solution on every push to main and every PR. It runs on Windows because Orleans integration tests use the Aspire testing builder which boots the full app.

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 11.0.x
          dotnet-quality: preview

      - name: Restore
        run: dotnet restore IAW.slnx

      - name: Build
        run: dotnet build IAW.slnx --no-restore --configuration Release

      - name: Test
        run: dotnet test IAW.slnx --no-build --configuration Release --verbosity normal
```

**Step 2: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add .github/workflows/ci.yml
git commit -m "ci: add build and test workflow"
```

---

### Task 6: Create GitHub Actions docs deployment workflow

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/.github/workflows/docs.yml`

**Step 1: Create docs deployment workflow**

This workflow builds the VitePress site from `website/` and deploys to GitHub Pages. It runs on push to main when files in `website/` change.

```yaml
name: Deploy Docs

on:
  push:
    branches: [main]
    paths:
      - 'website/**'
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: false

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: website/package-lock.json

      - name: Setup Pages
        uses: actions/configure-pages@v4

      - name: Install dependencies
        working-directory: website
        run: npm ci

      - name: Build
        working-directory: website
        run: npm run build

      - name: Upload artifact
        uses: actions/upload-pages-artifact@v3
        with:
          path: website/.vitepress/dist

  deploy:
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    needs: build
    runs-on: ubuntu-latest
    steps:
      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v4
```

**Step 2: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add .github/workflows/docs.yml
git commit -m "ci: add VitePress docs deployment workflow"
```

---

### Task 7: Initialize VitePress project

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/website/package.json`
- Create: `E:/IAW/InteractiveAgents/IAW/website/.vitepress/config.mts`
- Create: `E:/IAW/InteractiveAgents/IAW/website/.vitepress/theme/custom.css`

**Step 1: Create package.json**

```json
{
  "name": "iaw-docs",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vitepress dev",
    "build": "vitepress build",
    "preview": "vitepress preview"
  },
  "devDependencies": {
    "vitepress": "^2.0.0"
  }
}
```

**Step 2: Install dependencies**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npm install`
Expected: `node_modules/` created, `package-lock.json` generated

**Step 3: Add website/node_modules to .gitignore**

Append to `E:/IAW/InteractiveAgents/IAW/.gitignore`:

```
# VitePress
website/node_modules/
website/.vitepress/dist/
website/.vitepress/cache/
```

**Step 4: Create VitePress config**

Create `E:/IAW/InteractiveAgents/IAW/website/.vitepress/config.mts`:

```typescript
import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Interactive Agents',
  description: 'An open-source ecosystem of intelligent agents built on Orleans and .NET',
  base: '/IAW/',

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/IAW/logo.svg' }]
  ],

  themeConfig: {
    logo: '/logo.svg',

    nav: [
      { text: 'Guide', link: '/guide/' },
      { text: 'Reference', link: '/reference/' }
    ],

    sidebar: {
      '/guide/': [
        {
          text: 'Introduction',
          items: [
            { text: 'Getting Started', link: '/guide/' }
          ]
        },
        {
          text: 'Core Concepts',
          items: [
            { text: 'Architecture', link: '/guide/architecture' },
            { text: 'Building Agents', link: '/guide/agents' },
            { text: 'Notifications & Events', link: '/guide/notifications' }
          ]
        },
        {
          text: 'Integrations',
          items: [
            { text: 'Telegram Bot', link: '/guide/telegram' },
            { text: 'Testing', link: '/guide/testing' }
          ]
        }
      ],
      '/reference/': [
        {
          text: 'API Reference',
          items: [
            { text: 'Overview', link: '/reference/' }
          ]
        }
      ]
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/InteractiveAgents/IAW' }
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright 2026 InteractiveAgents'
    },

    search: {
      provider: 'local'
    }
  }
})
```

**Step 5: Create custom theme CSS**

Create `E:/IAW/InteractiveAgents/IAW/website/.vitepress/theme/custom.css`:

```css
:root {
  --vp-c-brand-1: #6366f1;
  --vp-c-brand-2: #818cf8;
  --vp-c-brand-3: #a5b4fc;
  --vp-c-brand-soft: rgba(99, 102, 241, 0.14);

  --vp-home-hero-name-color: transparent;
  --vp-home-hero-name-background: -webkit-linear-gradient(120deg, #6366f1, #a78bfa);

  --vp-home-hero-image-background-image: linear-gradient(-45deg, #6366f1 50%, #818cf8 50%);
  --vp-home-hero-image-filter: blur(44px);
}

.dark {
  --vp-c-brand-1: #818cf8;
  --vp-c-brand-2: #a5b4fc;
  --vp-c-brand-3: #c7d2fe;
  --vp-c-brand-soft: rgba(129, 140, 248, 0.14);
}
```

Create `E:/IAW/InteractiveAgents/IAW/website/.vitepress/theme/index.ts`:

```typescript
import DefaultTheme from 'vitepress/theme'
import './custom.css'

export default DefaultTheme
```

**Step 6: Verify VitePress starts**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npx vitepress dev`
Expected: Dev server starts at `http://localhost:5173/IAW/` (will show 404 until we add index.md in next task)
Press Ctrl+C to stop.

**Step 7: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add website/package.json website/package-lock.json website/.vitepress/ .gitignore
git commit -m "feat: initialize VitePress documentation site"
```

---

### Task 8: Create landing page and logo

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/website/index.md`
- Create: `E:/IAW/InteractiveAgents/IAW/website/public/logo.svg`

**Step 1: Create a simple SVG logo**

Create `E:/IAW/InteractiveAgents/IAW/website/public/logo.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" fill="none">
  <rect width="128" height="128" rx="24" fill="#6366f1"/>
  <text x="64" y="82" text-anchor="middle" font-family="system-ui, sans-serif" font-weight="700" font-size="56" fill="white">IA</text>
</svg>
```

This is a minimal indigo rounded square with "IA" in white. Can be replaced with a proper logo later.

**Step 2: Create landing page**

Create `E:/IAW/InteractiveAgents/IAW/website/index.md`:

```markdown
---
layout: home

hero:
  name: Interactive Agents
  text: Build intelligent agent systems on .NET
  tagline: An open-source ecosystem of agents that collaborate, remember, improve, and orchestrate tasks — powered by Orleans and Aspire.
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

features:
  - icon: 🧠
    title: Durable Memory
    details: Agents persist state, history, and events across restarts using Orleans Journaling. No external database required.
  - icon: 🔗
    title: Agent-to-Agent Communication
    details: Built-in pub/sub notifications, event streaming, and direct grain-to-grain calls. Agents discover and collaborate with each other.
  - icon: 🤖
    title: LLM Integration
    details: First-class support for OpenAI, Anthropic, and Ollama via Microsoft.Extensions.AI. Stream responses with SendAsync.
  - icon: 🛠️
    title: Generic Tools
    details: Define tools with DefineTools() and invoke them dynamically. Built on the AITool abstraction from Microsoft.Extensions.AI.
  - icon: 📡
    title: Observability
    details: OpenTelemetry tracing and metrics out of the box. Every agent operation is instrumented via ActivitySource and counters.
  - icon: ⚡
    title: Aspire-Native
    details: One-line setup with AddIAW(). Service discovery, health checks, and distributed tracing integrated via .NET Aspire.
---

## Quick Start

Install the NuGet packages:

```bash
dotnet add package IAW.Core
```

Create your first agent:

```csharp
public class GreeterAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    public override string DisplayName => "Greeter";
    public override string SystemPrompt => "You are a friendly greeter.";
}
```

Configure in your Aspire AppHost:

```csharp
var orleans = builder.AddOrleans("cluster")
    .WithMemoryGrainStorage("agent-values")
    .WithMemoryGrainStorage("agent-history")
    .WithMemoryGrainStorage("agent-events")
    .WithMemoryGrainStorage("agent-subscriptions")
    .WithMemoryGrainStorage("agent-notifications")
    .WithMemoryGrainStorage("agent-tracking");
```
```

**Step 3: Verify landing page renders**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npx vitepress dev`
Expected: Landing page renders at `http://localhost:5173/IAW/` with hero, features grid, and quick start code.
Press Ctrl+C to stop.

**Step 4: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add website/index.md website/public/
git commit -m "feat: add landing page with hero, features, and quick start"
```

---

### Task 9: Create Guide pages (Getting Started, Architecture, Agents)

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/website/guide/index.md`
- Create: `E:/IAW/InteractiveAgents/IAW/website/guide/architecture.md`
- Create: `E:/IAW/InteractiveAgents/IAW/website/guide/agents.md`

**Step 1: Create Getting Started page**

Create `E:/IAW/InteractiveAgents/IAW/website/guide/index.md`:

```markdown
# Getting Started

IAW (Interactive Agents Web) is an Orleans-based multi-agent runtime for building intelligent agent systems on .NET.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- An IDE (Visual Studio, Rider, or VS Code)

## Installation

Add the core package to your project:

```bash
dotnet add package IAW.Core
```

## Your First Agent

Every agent extends the `Agent` base class and receives durable state collections via constructor injection:

```csharp
using Core;
using Orleans.Journaling;

public class TodoAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    public override string DisplayName => "Todo Manager";
    public override string SystemPrompt => "You help manage todo lists.";
}
```

## Aspire Integration

IAW is designed to run inside a .NET Aspire AppHost. Configure Orleans grain storage for each durable state collection:

```csharp
var orleans = builder.AddOrleans("cluster")
    .WithMemoryGrainStorage("agent-values")
    .WithMemoryGrainStorage("agent-history")
    .WithMemoryGrainStorage("agent-events")
    .WithMemoryGrainStorage("agent-subscriptions")
    .WithMemoryGrainStorage("agent-notifications")
    .WithMemoryGrainStorage("agent-tracking");

builder.AddProject<Projects.MyApp>("myapp")
    .WithReference(orleans.AsClient());
```

## Interacting with Agents

Agents are Orleans grains identified by a string key. Get a reference and call behavior methods:

```csharp
var agent = grainFactory.GetGrain<IAgent>("my-todo-agent");

// Add to conversation history
await agent.AddHistoryAsync("user", "Add 'buy groceries' to my list");

// Set state
await agent.SetStateAsync("last-command", "add-todo");

// Publish an event
await agent.PublishEventAsync("todo-added", "{\"item\": \"buy groceries\"}");
```

## Next Steps

- [Architecture](/guide/architecture) — Understand how agents work under the hood
- [Building Agents](/guide/agents) — Deep dive into the Agent base class
- [Notifications & Events](/guide/notifications) — Agent-to-agent communication
```

**Step 2: Create Architecture page**

Create `E:/IAW/InteractiveAgents/IAW/website/guide/architecture.md`:

```markdown
# Architecture

IAW agents are [Orleans grains](https://learn.microsoft.com/dotnet/orleans/) — virtual actors that live in a distributed cluster.

## The Agent Class

Every agent extends `Agent`, which extends Orleans `DurableGrain`:

```
Agent : DurableGrain, IAgent, IRemindable
```

`DurableGrain` comes from `Microsoft.Orleans.Journaling` and provides durable, journaled state. State survives grain deactivation and silo restarts.

## Durable State Collections

Each agent has 6 durable state collections injected via the `[Memory]` attribute:

| Collection | Type | Purpose |
|-----------|------|---------|
| `agent-values` | `IDurableDictionary<string, string>` | Key-value state (config, flags, counters) |
| `agent-history` | `IDurableList<AgentHistoryEntry>` | Conversation history (role + content + timestamp) |
| `agent-events` | `IDurableList<AgentEventRecord>` | Published events log |
| `agent-subscriptions` | `IDurableDictionary<string, List<string>>` | Topic → subscriber agent IDs |
| `agent-notifications` | `IDurableList<NotificationRecord>` | Received notifications inbox |
| `agent-tracking` | `IDurableDictionary<string, AgentTrackingStatus>` | Timer-based tracking state |

## Behavior Interfaces

`IAgent` composes 8 behavior interfaces:

```csharp
public interface IAgent :
    IGrainWithStringKey,
    IAgentMetadataBehavior,      // GetMetadataAsync
    IAgentStateBehavior,         // SetStateAsync, GetStateValueAsync, IncrementAsync
    IAgentHistoryBehavior,       // AddHistoryAsync, GetHistoryAsync
    IAgentEventsBehavior,        // PublishEventAsync, GetEventsAsync
    IAgentNotificationsBehavior, // SubscribeAsync, NotifyAsync, ReceiveNotificationAsync
    IAgentTrackingBehavior,      // StartTrackingAsync, StopTrackingAsync
    IAgentToolsBehavior,         // InvokeToolAsync
    IAgentStreamsBehavior;       // PublishStreamAsync
```

Each behavior is independently testable and represents a distinct agent capability.

## LLM Integration

The `Agent` class integrates with LLMs via `Microsoft.Extensions.AI`:

```csharp
// In your agent subclass
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);
    Activate(chatClient); // IChatClient injected via constructor
}
```

Once activated, call `SendAsync` to get streaming LLM responses:

```csharp
await foreach (var chunk in SendAsync("Hello!", ct))
{
    // Each chunk is a string fragment from the LLM
}
```

`SendAsync` is a `public virtual` method on the `Agent` class (not on `IAgent`), because `IAsyncEnumerable` doesn't work across Orleans grain boundaries. For grain-to-grain LLM communication, agents use notifications.

## Observability

Every agent operation emits OpenTelemetry traces and metrics through `AgentObservability`:

- **ActivitySource** `IAW.Agent` — spans for SendAsync, tool invocations, notifications
- **Counters** — messages sent, tokens used, tool invocations, errors
```

**Step 3: Create Building Agents page**

Create `E:/IAW/InteractiveAgents/IAW/website/guide/agents.md`:

```markdown
# Building Agents

This guide covers the `Agent` base class and how to build custom agents.

## Minimal Agent

The simplest agent just extends `Agent` with the required durable state parameters:

```csharp
public class MyAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    public override string DisplayName => "My Agent";
}
```

## Adding LLM Support

Inject an `IChatClient` and activate it:

```csharp
public class ChatAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking,
    IChatClient chatClient)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    public override string DisplayName => "Chat Agent";
    public override string SystemPrompt => "You are a helpful assistant.";

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        Activate(chatClient);
    }
}
```

## Defining Tools

Override `DefineTools()` to give your agent callable tools:

```csharp
protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create((string city) =>
    {
        return city switch
        {
            "London" => "15°C, cloudy",
            "Tokyo" => "22°C, sunny",
            _ => "Unknown city"
        };
    }, "GetWeather", "Gets the current weather for a city")
];
```

Tools are automatically available to the LLM during `SendAsync` calls and can be invoked remotely via `InvokeToolAsync`:

```csharp
var result = await agent.InvokeToolAsync("GetWeather",
    new Dictionary<string, string> { ["city"] = "London" });
// Returns: "15°C, cloudy"
```

## Virtual Properties

| Property | Default | Purpose |
|----------|---------|---------|
| `DisplayName` | Grain ID | Human-readable name shown in metadata |
| `SystemPrompt` | `string.Empty` | System message sent to LLM before conversation |
| `Id` | (from grain key) | The grain's string identity |

## State Management

Agents have built-in key-value state:

```csharp
// Set a value
await SetStateAsync("user-preference", "dark-mode");

// Get a value
var pref = await GetStateValueAsync("user-preference");

// Atomic counter
var visitCount = await IncrementAsync("visit-count");
```

## Tracking (Timers)

Start periodic ticks via Orleans reminders:

```csharp
// Tick every 30 seconds, max 10 times
await StartTrackingAsync(TimeSpan.FromSeconds(30), maxTicks: 10);

// Check status
var status = await GetTrackingStatusAsync();
// status.IsTracking, status.TickCount, status.StartedAtUtc

// Stop early
await StopTrackingAsync();
```
```

**Step 4: Verify pages render**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npx vitepress dev`
Expected: Navigate to `/IAW/guide/`, `/IAW/guide/architecture`, `/IAW/guide/agents` — all render with sidebar navigation.
Press Ctrl+C to stop.

**Step 5: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add website/guide/
git commit -m "docs: add getting started, architecture, and agents guide"
```

---

### Task 10: Create remaining Guide pages (Notifications, Telegram, Testing)

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/website/guide/notifications.md`
- Create: `E:/IAW/InteractiveAgents/IAW/website/guide/telegram.md`
- Create: `E:/IAW/InteractiveAgents/IAW/website/guide/testing.md`

**Step 1: Create Notifications & Events page**

Create `E:/IAW/InteractiveAgents/IAW/website/guide/notifications.md`:

```markdown
# Notifications & Events

Agents communicate through two mechanisms: events (broadcast) and notifications (targeted).

## Events

Events are published to Orleans streams and received by subscribers:

```csharp
// Publish an event
await agent.PublishEventAsync("order-placed", "{\"orderId\": 123}");

// Get event history
var events = await agent.GetEventsAsync();
```

Events are persisted in the agent's durable event log.

## Notifications

Notifications are targeted messages between agents using a topic-based pub/sub system.

### Subscribe to a topic

```csharp
// Agent "order-processor" subscribes to notifications from "order-agent" on topic "orders"
var orderAgent = grainFactory.GetGrain<IAgent>("order-agent");
await orderAgent.SubscribeAsync("orders", "order-processor");
```

### Send a notification

```csharp
// Simple string payload
await orderAgent.NotifyAsync("orders", "{\"orderId\": 123, \"status\": \"confirmed\"}");
```

### Typed notifications with NotificationEnvelope

For richer metadata, use `NotificationEnvelope`:

```csharp
var envelope = NotificationJson.CreateEnvelope(
    topic: "orders",
    payload: new OrderConfirmed { OrderId = 123 },
    schema: "order-confirmed",
    schemaVersion: "1.0",
    correlationId: "tx-abc-123");

await orderAgent.NotifyAsync(envelope);
```

The envelope includes topic, payload, content type, schema info, message ID, correlation ID, headers, and timestamp.

### Reading typed payloads

```csharp
var notifications = await agent.GetNotificationsAsync();
foreach (var notification in notifications)
{
    var order = notification.ReadPayload<OrderConfirmed>();
}
```

## Streams

For real-time event streaming via Orleans Streams:

```csharp
await agent.PublishStreamAsync("agent-events", streamId, "event payload");
```
```

**Step 2: Create Telegram Bot page**

Create `E:/IAW/InteractiveAgents/IAW/website/guide/telegram.md`:

```markdown
# Telegram Bot Integration

IAW includes a Telegram Bot client built as an Orleans grain that extends `Agent`.

## Overview

`TelegramBotGrain` is a full-featured Telegram bot that:
- Manages forum topics (Assistant, Notifications, Settings)
- Routes messages to the correct agent based on topic
- Supports inline keyboards, reactions, and message editing
- Handles webhooks for receiving updates

## Setup

1. Create a bot via [@BotFather](https://t.me/BotFather) and get your token
2. Enable Topics in your group chat settings
3. Configure the bot token in your Aspire AppHost:

```csharp
builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(orleans.AsClient())
    .WithEnvironment("Telegram__BotToken", builder.AddParameter("telegram-token", secret: true));
```

## How It Works

When a user sends `/start`, the bot:
1. Creates forum topics (Assistant, Notifications, Settings)
2. Saves the topic registry in durable state
3. Sends a welcome message with navigation keyboard

Messages in the Assistant topic are forwarded to a `personal-assistant` agent. The bot supports:

- **Text messages** — routed based on forum topic
- **Inline keyboards** — navigation between topics
- **Callbacks** — button press handling with `AnswerCallback`
- **Reactions** — emoji reactions on received messages
- **Typing indicators** — sent before processing

## API

The `ITelegramBot` grain interface exposes:

| Method | Purpose |
|--------|---------|
| `HandleUpdate` | Process incoming Telegram update |
| `SendText` | Send plain text message |
| `SendMarkdown` | Send MarkdownV2 formatted message |
| `SendKeyboard` | Send message with inline keyboard |
| `EditMessage` | Edit existing message text/buttons |
| `SetReaction` | Add emoji reaction to message |
| `PinMessage` | Pin a message in chat |
| `CreateTopic` | Create a forum topic |
| `SetWebhook` | Configure webhook URL |
```

**Step 3: Create Testing page**

Create `E:/IAW/InteractiveAgents/IAW/website/guide/testing.md`:

```markdown
# Testing Agents

IAW provides two testing approaches: Orleans TestCluster for unit tests and Aspire for integration tests.

## Unit Tests (TestCluster)

Unit tests run agents in an in-memory Orleans cluster using `TestClusterBuilder`:

```csharp
public class AgentsSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("agent-values")
            .AddMemoryGrainStorage("agent-history")
            .AddMemoryGrainStorage("agent-events")
            .AddMemoryGrainStorage("agent-subscriptions")
            .AddMemoryGrainStorage("agent-notifications")
            .AddMemoryGrainStorage("agent-tracking")
            .AddMemoryStreams("agents")
            .AddMemoryGrainStorageAsDefault()
            .UseInMemoryReminderService();
    }
}
```

Write tests against the `IAgent` interface:

```csharp
[Fact]
public async Task Agent_SetsAndGetsState()
{
    var agent = cluster.GrainFactory.GetGrain<IAgent>("test-agent");

    await agent.SetStateAsync("key", "value");
    var result = await agent.GetStateValueAsync("key");

    Assert.Equal("value", result);
}
```

## Integration Tests (Aspire)

Integration tests boot the full Aspire app using `DistributedApplicationTestingBuilder`:

```csharp
var appHost = await DistributedApplicationTestingBuilder
    .CreateAsync<Projects.IAW_AppHost>();

await using var app = await appHost.BuildAsync();
await app.StartAsync();

await app.ResourceNotifications
    .WaitForResourceHealthyAsync("samples");
```

Then make HTTP requests against the running app:

```csharp
var httpClient = app.CreateHttpClient("samples");
var response = await httpClient.GetAsync("/samples/orleans-agent/metadata");

Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

## Test Project Setup

Add test projects to your solution:

```xml
<PackageReference Include="xunit.v3" />
<PackageReference Include="Microsoft.Orleans.TestingHost" />
<PackageReference Include="Microsoft.NET.Test.Sdk" />
```

For integration tests, also add:

```xml
<PackageReference Include="Aspire.Hosting.Testing" />
```

## Running Tests

```bash
# All tests
dotnet test IAW.slnx

# Unit tests only
dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj

# Integration tests only (boots full Aspire app)
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj
```
```

**Step 4: Verify all pages render**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npx vitepress dev`
Expected: All 6 guide pages render correctly with sidebar navigation.
Press Ctrl+C to stop.

**Step 5: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add website/guide/
git commit -m "docs: add notifications, telegram, and testing guides"
```

---

### Task 11: Create API Reference page

**Files:**
- Create: `E:/IAW/InteractiveAgents/IAW/website/reference/index.md`

**Step 1: Create API Reference page**

Create `E:/IAW/InteractiveAgents/IAW/website/reference/index.md`:

```markdown
# API Reference

## IAgent

The main agent interface, composed of 8 behavior interfaces:

```csharp
public interface IAgent :
    IGrainWithStringKey,
    IAgentMetadataBehavior,
    IAgentStateBehavior,
    IAgentHistoryBehavior,
    IAgentEventsBehavior,
    IAgentNotificationsBehavior,
    IAgentTrackingBehavior,
    IAgentToolsBehavior,
    IAgentStreamsBehavior;
```

## Behavior Interfaces

### IAgentMetadataBehavior

```csharp
Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default);
```

### IAgentStateBehavior

```csharp
Task SetStateAsync(string key, string value, CancellationToken ct = default);
Task<string?> GetStateValueAsync(string key, CancellationToken ct = default);
Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default);
Task<int> IncrementAsync(string counterKey, CancellationToken ct = default);
```

### IAgentHistoryBehavior

```csharp
Task AddHistoryAsync(string role, string content, CancellationToken ct = default);
Task<List<AgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default);
```

### IAgentEventsBehavior

```csharp
Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default);
Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default);
```

### IAgentNotificationsBehavior

```csharp
Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default);
Task NotifyAsync(string topic, string payload, CancellationToken ct = default);
Task NotifyAsync(NotificationEnvelope notification, CancellationToken ct = default);
Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default);
Task ReceiveNotificationAsync(NotificationEnvelope notification, CancellationToken ct = default);
Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default);
```

### IAgentTrackingBehavior

```csharp
Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default);
Task StopTrackingAsync(CancellationToken ct = default);
Task<AgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default);
```

### IAgentToolsBehavior

```csharp
Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);
```

### IAgentStreamsBehavior

```csharp
Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);
```

## Contract Types

### AgentMetadata

```csharp
public sealed class AgentMetadata
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public List<string> Capabilities { get; set; }
}
```

### AgentHistoryEntry

```csharp
public sealed class AgentHistoryEntry
{
    public string Role { get; set; }
    public string Content { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}
```

### AgentEventRecord

```csharp
public sealed class AgentEventRecord
{
    public string Name { get; set; }
    public string? Payload { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}
```

### NotificationEnvelope

```csharp
public sealed class NotificationEnvelope
{
    public string Topic { get; set; }
    public string Payload { get; set; }
    public string ContentType { get; set; }
    public string? Schema { get; set; }
    public string? SchemaVersion { get; set; }
    public string MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Headers { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}
```

### NotificationRecord

```csharp
public sealed class NotificationRecord
{
    public string Topic { get; set; }
    public string Payload { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string ContentType { get; set; }
    public string? Schema { get; set; }
    public string? SchemaVersion { get; set; }
    public string MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Headers { get; set; }
}
```

### AgentTrackingStatus

```csharp
public sealed class AgentTrackingStatus
{
    public bool IsTracking { get; set; }
    public int TickCount { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public TimeSpan Interval { get; set; }
    public int MaxTicks { get; set; }
}
```

## Helper Classes

### NotificationJson

```csharp
// Create a typed notification envelope
var envelope = NotificationJson.CreateEnvelope("topic", payload,
    schema: "my-schema", schemaVersion: "1.0");

// Read typed payload from envelope or record
var data = envelope.ReadPayload<MyType>();
var data = record.ReadPayload<MyType>();
```
```

**Step 2: Verify**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npx vitepress dev`
Expected: Reference page renders at `/IAW/reference/`.
Press Ctrl+C to stop.

**Step 3: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add website/reference/
git commit -m "docs: add API reference page"
```

---

### Task 12: Write the README.md

**Files:**
- Modify: `E:/IAW/InteractiveAgents/IAW/README.md`

**Step 1: Replace README.md**

The current README is just `# IAW`. Replace it entirely with a proper open-source README. The content below uses the CI workflow badge (which will show "failing" until the workflow actually runs on GitHub, but the badge URL is correct):

```markdown
# Interactive Agents (IAW)

[![CI](https://github.com/InteractiveAgents/IAW/actions/workflows/ci.yml/badge.svg)](https://github.com/InteractiveAgents/IAW/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-11.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/11.0)

An open-source ecosystem of intelligent agents that collaborate, remember, improve, and orchestrate tasks — powered by Orleans and .NET Aspire.

## Features

- **Durable Memory** — Agents persist state, history, and events across restarts using Orleans Journaling
- **Agent-to-Agent Communication** — Built-in pub/sub notifications, event streaming, and direct grain calls
- **LLM Integration** — OpenAI, Anthropic, and Ollama via Microsoft.Extensions.AI
- **Generic Tools** — Define and invoke tools dynamically with `DefineTools()` + `InvokeToolAsync`
- **Observability** — OpenTelemetry tracing and metrics out of the box
- **Aspire-Native** — One-line setup with `AddIAW()`, service discovery, health checks

## Quick Start

```bash
dotnet add package IAW.Core
```

```csharp
public class GreeterAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    public override string DisplayName => "Greeter";
    public override string SystemPrompt => "You are a friendly greeter.";
}
```

## Documentation

Full documentation at [interactiveagents.github.io/IAW](https://interactiveagents.github.io/IAW).

## Building from Source

```bash
git clone https://github.com/InteractiveAgents/IAW.git
cd IAW
dotnet build IAW.slnx
dotnet test IAW.slnx
```

## Project Structure

| Path | Description |
|------|-------------|
| `src/Core` | Agent base class, behaviors, contracts, LLM integration |
| `src/IAW.AppHost` | Aspire AppHost orchestration |
| `src/IAW.ServiceDefaults` | OpenTelemetry and Aspire service defaults |
| `src/Clients.Telegram.Bot` | Telegram Bot agent integration |
| `src/IAW.MCP` | Model Context Protocol server bridge |
| `samples/Samples` | Sample agent endpoints |
| `test/Agents.Tests` | Orleans TestCluster unit tests |
| `test/Integration.Tests` | Aspire integration tests |
| `website/` | VitePress documentation site |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup and guidelines.

## License

[MIT](LICENSE)
```

**Step 2: Commit**

```bash
cd E:/IAW/InteractiveAgents/IAW
git add README.md
git commit -m "docs: write comprehensive README"
```

---

### Task 13: Build VitePress site and run full verification

**Step 1: Install website dependencies**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npm install`
Expected: Dependencies installed successfully

**Step 2: Build VitePress site**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npm run build`
Expected: Build succeeds, output in `website/.vitepress/dist/`

**Step 3: Preview the built site**

Run: `cd E:/IAW/InteractiveAgents/IAW/website && npm run preview`
Expected: Preview server starts. Verify:
- Landing page renders with hero, features, and quick start
- All 6 guide pages are accessible and have sidebar navigation
- Reference page shows all API interfaces and types
- Dark mode toggle works
- Search works (type a term, get results)
- GitHub link in navbar works
Press Ctrl+C to stop.

**Step 4: Build the .NET solution**

Run: `dotnet build E:/IAW/InteractiveAgents/IAW/IAW.slnx`
Expected: 0 warnings, 0 errors

**Step 5: Run all .NET tests**

Run: `dotnet test E:/IAW/InteractiveAgents/IAW/IAW.slnx`
Expected: All tests pass (unit + integration)

**Step 6: Commit any fixes**

If any issues found, fix and commit. Otherwise:

```bash
cd E:/IAW/InteractiveAgents/IAW
git add -A
git commit -m "chore: OSS website and project polish complete"
```
