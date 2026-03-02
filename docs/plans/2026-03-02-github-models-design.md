# GitHub Models Integration Design

## Goal

Add GitHub Models as an LLM provider, reusing the existing `IChatClient` / `[Llm<>]` infrastructure. GitHub Models exposes an OpenAI-compatible API at `https://models.inference.ai.azure.com` using a GitHub PAT as the API key.

## Models

- `GitHubGpt4oMini` — `gpt-4o-mini` via GitHub Models endpoint
- `GitHubGpt4o` — `gpt-4o` via GitHub Models endpoint

## Approach: OpenAI SDK with Custom Endpoint

No new NuGet packages. The existing `OpenAI` SDK supports custom endpoints via `OpenAIClientOptions.Endpoint`. The `_gitHubTokenParam` already exists in `IAWExtensions` — it just needs proper wiring.

## Changes

### 1. ProviderType.cs
Add `GitHub` to the enum.

### 2. LlmConfig.cs
Add `GitHubModelsApiKey = "AI:LLM:GitHubToken"` constant and `GitHubModelsEndpoint = "https://models.inference.ai.azure.com"`.

### 3. New model files (src/Core/AI/Models/)
- `GitHubGpt4oMini.cs` — `Provider = ProviderType.GitHub`, `Id = "gpt-4o-mini"`
- `GitHubGpt4o.cs` — `Provider = ProviderType.GitHub`, `Id = "gpt-4o"`

### 4. LLMModel.EnsureAllModelsLoaded()
Add references to new model instances.

### 5. LlmRegistration.cs
- Add `CreateGitHubModelsClient()` — uses `OpenAIClient` with custom endpoint + GitHub token
- Add `ProviderType.GitHub` case to `CreateChatClient()` switch
- Add `ProviderType.GitHub` case to `IsProviderConfigured()` — checks `GitHubModelsApiKey`

### 6. IAWExtensions.cs
- Make `_gitHubTokenParam` / `AI__LLM__GitHubToken` injection conditional on `_declaredProviders.Contains(ProviderType.GitHub)` instead of unconditional
- Remove the always-on GitHub token injection (currently lines ~98-99)

### 7. GitHubTestAgent grain (samples/Samples/)
New grain with `[Llm<GitHubGpt4oMini>]` injection to prove the integration.

### 8. Sample endpoint (samples/Samples/Program.cs)
New `/github-models` GET endpoint that sends a test prompt via `GitHubTestAgent`.

### 9. AppHost.cs
Add `.WithLLM<GitHubGpt4oMini>()` to the IAW builder chain.

## What stays the same
- All existing `IChatClient`, `[Llm<>]`, `LlmAttributeMapper<>` infrastructure
- All existing models and providers
- Test framework (uses `MockChatClient`)
