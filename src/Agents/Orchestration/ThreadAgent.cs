using System.Text.Json;
using Core;
using Core.Context;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using AgentResponse = global::Core.UI.AgentResponse;

namespace IAW.Agents.Orchestration;

[GrainType(IAWConstants.GrainTypes.Thread)]
public class ThreadAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    ILogger<ThreadAgent> logger)
    : Agent<IThread>(durableState, chatClient), IThread
{
    private const string CallbackPrefix = "cb:";

    protected override int MaxHistoryMessages => 20;

    private IReadOnlyList<IAgentContextProvider>? _contextProviders;

    protected override IReadOnlyList<IAgentContextProvider> GetContextProviders()
    {
        if (_contextProviders is not null) return _contextProviders;

        var providers = new List<IAgentContextProvider>
        {
            new UserContextProvider(GrainFactory)
        };

        var qdrant = ServiceProvider.GetService<QdrantClient>();
        var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (qdrant is not null && embeddings is not null)
            providers.Add(new RAGContextProvider(qdrant, embeddings));

        var memoryAgents = ServiceProvider.GetService<IReadOnlyList<IMemoryAgent>>();
        if (memoryAgents is not null && memoryAgents.Count > 0)
            providers.Add(new MemoryContextProvider(memoryAgents));

        _contextProviders = providers;
        return _contextProviders;
    }

    protected override IReadOnlyList<AITool> DefineAdditionalTools()
    {
        return [
            AIFunctionFactory.Create(SendToAgentAsync, "SendToAgent",
                "Send a task to a specific agent by name. The agent handles it autonomously " +
                "with its own LLM and tools. Available agents: Shell, DotNet, FileSystem, Git, Roslyn, GitHub, Aspire."),

            AIFunctionFactory.Create(OrchestrateAsync, "Orchestrate",
                "For complex multi-step tasks requiring coordination across multiple agents. " +
                "NOT needed for single build/run/read/git tasks — use SendToAgent instead."),

            AIFunctionFactory.Create(SelfImproveAsync, "SelfImprove",
                "Fix a bug or improve the IAW system itself. Reads source code, analyzes the issue, " +
                "writes a fix, builds, tests, commits, and deploys via Aspire restart. " +
                "Use when the user reports a bug in the agent system or asks to improve/fix behavior.")
        ];
    }

    private async Task<string> SendToAgentAsync(string agentName, string request, CancellationToken ct = default)
    {
        logger.LogInformation("SendToAgent: {Agent} for: {Request}",
            agentName, request[..Math.Min(80, request.Length)]);

        var interfaceType = AgentInterfaceResolver.ResolveByDisplayName(agentName)
                         ?? AgentInterfaceResolver.Resolve(agentName);
        if (interfaceType is null)
            return $"Unknown agent: {agentName}. Available: Shell, DotNet, FileSystem, Git, Roslyn, GitHub.";

        var threadId = this.GetPrimaryKeyString();
        var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");

        try
        {
            var result = await agent.GetResponse(request, ct);
            return result.Length > 4000
                ? result[..4000] + "\n...(truncated)"
                : result;
        }
        catch (OperationCanceledException)
        {
            return $"Agent {agentName} timed out. Try a simpler request or a different agent.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendToAgent: {Agent} failed", agentName);
            var suggestion = agentName switch
            {
                "DotNet" => "Try Shell agent for raw dotnet CLI commands, or check the project path.",
                "Shell" => "Check command syntax. For .NET operations, use DotNet agent instead.",
                "FileSystem" => "Check file path exists. Use absolute paths.",
                "Git" => "Check repository path. Ensure it's a valid git repo.",
                "Aspire" => "Aspire MCP may not be connected. Try again after restart.",
                "Roslyn" => "Check that the workspace is set and contains C# code.",
                _ => "Try a different agent or rephrase the request."
            };
            return $"Agent {agentName} failed: {ex.Message}\nSuggestion: {suggestion}";
        }
    }

    private async Task<string> SelfImproveAsync(string issueDescription, CancellationToken ct = default)
    {
        logger.LogInformation("SelfImprove: {Issue}", issueDescription[..Math.Min(80, issueDescription.Length)]);
        var steps = new global::System.Text.StringBuilder();
        var iawRoot = @"E:\IAW";

        try
        {
            steps.AppendLine("## Step 1: Reading traces...");
            var traces = await SendToAgentAsync("Aspire",
                $"Get recent traces for the assistant resource to help diagnose: {issueDescription}", ct);
            steps.AppendLine(traces.Length > 500 ? traces[..500] + "..." : traces);

            steps.AppendLine("\n## Step 2: Analyzing issue...");
            var analysis = await SendToAgentAsync("Roslyn",
                $"Based on this issue description, which source files in {iawRoot}/src/ are most likely involved? " +
                $"Issue: {issueDescription}\nRecent traces: {traces[..Math.Min(500, traces.Length)]}", ct);
            steps.AppendLine(analysis.Length > 500 ? analysis[..500] + "..." : analysis);

            steps.AppendLine("\n## Step 3: Reading source code...");
            var code = await SendToAgentAsync("FileSystem",
                $"Read the most relevant source files for this issue. {analysis}", ct);
            steps.AppendLine($"Read {code.Length} chars of source code");

            steps.AppendLine("\n## Step 4: Writing fix...");
            var fix = await SendToAgentAsync("Roslyn",
                $"Here is the issue: {issueDescription}\nHere is the code:\n{code[..Math.Min(3000, code.Length)]}\n" +
                "Generate the fixed code. Return ONLY the complete fixed file content.", ct);

            if (fix.Contains("```"))
            {
                steps.AppendLine("Fix generated. Writing to file...");
                var writeResult = await SendToAgentAsync("FileSystem", $"Write the fix:\n{fix}", ct);
                steps.AppendLine(writeResult);
            }
            else
            {
                steps.AppendLine($"Analysis: {fix[..Math.Min(300, fix.Length)]}");
            }

            steps.AppendLine("\n## Step 5: Building...");
            var buildResult = await SendToAgentAsync("DotNet", $"Build the solution at {iawRoot}", ct);
            steps.AppendLine(buildResult);

            if (buildResult.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                buildResult.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                steps.AppendLine("\n**Build failed. Fix not applied.**");
                return steps.ToString();
            }

            steps.AppendLine("\n## Step 6: Running tests...");
            var testResult = await SendToAgentAsync("DotNet", $"Run tests for {iawRoot}", ct);
            steps.AppendLine(testResult);

            steps.AppendLine("\n## Step 7: Committing...");
            var commitResult = await SendToAgentAsync("Git",
                $"In {iawRoot}, commit all changes with message: fix: {issueDescription[..Math.Min(50, issueDescription.Length)]}", ct);
            steps.AppendLine(commitResult);

            steps.AppendLine("\n## Step 8: Deploying...");
            var deployResult = await SendToAgentAsync("Aspire", "Restart the assistant resource to deploy the fix", ct);
            steps.AppendLine(deployResult);

            steps.AppendLine("\n## Done! Fix applied and deployed.");
            return steps.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SelfImprove failed");
            steps.AppendLine($"\n**Error during self-improvement: {ex.Message}**");
            return steps.ToString();
        }
    }

    private async Task<string> OrchestrateAsync(string request, CancellationToken ct = default)
    {
        var taskId = $"dlg-{Guid.NewGuid().ToString("N")[..8]}";
        logger.LogInformation("Orchestrate: executing {TaskId} for: {Request}",
            taskId, request[..Math.Min(80, request.Length)]);

        return await ExecuteDelegation(taskId, request, ct);
    }

    private async Task<string> ExecuteSelection(SelectionResult selection, string request, CancellationToken ct)
    {
        var threadId = this.GetPrimaryKeyString();
        var lastUserMsg = History.LastOrDefault(m => m.Role == "user");
        var userMessage = lastUserMsg?.Text ?? request;

        if (selection.SelectedAgents.Count == 1)
        {
            var agentInterfaceName = selection.SelectedAgents[0];
            var interfaceType = AgentInterfaceResolver.Resolve(agentInterfaceName);
            if (interfaceType is null)
                return $"Could not resolve agent: {agentInterfaceName}";

            var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");
            return await agent.GetResponse(request, ct);
        }

        var orchestrator = GrainFactory.Get<ICodeOrchestrator>(threadId);
        var selectorPlan = selection.Plan ?? $"Agents: {string.Join(", ", selection.SelectedAgents)}";
        var plan = $"USER REQUEST: {userMessage}\n\nPLAN:\n{selectorPlan}";
        var result = await orchestrator.ExecuteCodeOrchestration(plan, selection.SelectedAgents, threadId, ct);
        return JsonSerializer.Serialize(result);
    }

    private static string FormatClarificationResponse(SelectionResult result)
    {
        if (result.Questions is null or { Count: 0 })
            return "I need more information to proceed. Could you clarify your request?";

        var sb = new global::System.Text.StringBuilder("I need some clarification:\n\n");
        foreach (var q in result.Questions)
        {
            sb.AppendLine($"- {q.Text}");
            if (q.Options is { Count: > 0 })
                sb.AppendLine($"  Options: {string.Join(", ", q.Options)}");
        }
        return sb.ToString();
    }

    protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
    {
        if (!job.Prompt.StartsWith(IAWConstants.DelegationPrefix))
        {
            await base.OnScheduledJobDueAsync(job, ct);
            return;
        }

        var request = job.Prompt[IAWConstants.DelegationPrefix.Length..];
        var result = await ExecuteDelegation(job.Name, request, ct);

        var updated = job with { LastRunAt = DateTimeOffset.UtcNow, LastResult = result };
        ScheduledJobs[job.Name] = updated;
    }

    private async Task<string> ExecuteDelegation(string taskId, string request, CancellationToken ct)
    {
        logger.LogInformation("Delegation: executing {TaskId} for: {Request}",
            taskId, request[..Math.Min(80, request.Length)]);

        string delegationResult;
        using var selectorTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var selectorLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, selectorTimeout.Token);
        try
        {
            var selector = GrainFactory.Get<IAgentSelector>();
            var selection = await selector.SelectAsync(request, selectorLinked.Token);

            logger.LogInformation("Delegation: selector returned Status={Status}, Agents=[{Agents}]",
                selection.Status, string.Join(",", selection.SelectedAgents));

            delegationResult = selection.Status switch
            {
                SelectionStatus.Ready => await ExecuteSelection(selection, request, ct),
                SelectionStatus.CannotHandle => selection.Plan ?? "The agent system cannot handle this request.",
                SelectionStatus.NeedsClarification => FormatClarificationResponse(selection),
                _ => "Unexpected selection status."
            };
        }
        catch (OperationCanceledException) when (selectorTimeout.IsCancellationRequested)
        {
            logger.LogWarning("Delegation: selector timed out for {TaskId}", taskId);
            delegationResult = "Delegation timed out during agent selection. Please try again.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delegation: FAILED {TaskId}", taskId);
            delegationResult = $"Delegation failed: {ex.GetType().Name}: {ex.Message}";
        }

        logger.LogInformation("Delegation: completed {TaskId}, result length: {Length}",
            taskId, delegationResult.Length);

        var safeResult = TruncateOrchestrationResultSafely(delegationResult);

        await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, string>
        {
            [IAWConstants.PayloadKeys.ProjectKey] = this.GetPrimaryKeyString(),
            [IAWConstants.PayloadKeys.JobName] = taskId,
            [IAWConstants.PayloadKeys.Result] = safeResult
        }, CancellationToken.None);

        return delegationResult;
    }

    private static string TruncateOrchestrationResultSafely(string resultPayload)
    {
        const int maxLength = 4000;
        if (resultPayload.Length <= maxLength)
            return resultPayload;

        // Try to truncate ErrorDetail inside the JSON structure before re-serializing
        try
        {
            var parsed = JsonSerializer.Deserialize<OrchestrationResult>(resultPayload);
            if (parsed is not null)
            {
                var truncatedError = parsed.ErrorDetail is { Length: > 500 }
                    ? parsed.ErrorDetail[..500] + "...(truncated)"
                    : parsed.ErrorDetail;
                var truncatedSummary = parsed.Summary is { Length: > 1000 }
                    ? parsed.Summary[..1000] + "...(truncated)"
                    : parsed.Summary;
                var compact = parsed with { ErrorDetail = truncatedError, Summary = truncatedSummary };
                return JsonSerializer.Serialize(compact);
            }
        }
        catch (JsonException) { }

        // Non-JSON result: truncate the plain text safely
        return resultPayload[..maxLength] + "\n...(truncated)";
    }

    public async Task RegisterCallback(string callbackId, string grainType, string grainId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var value = $"{grainType}|{grainId}|{expiresAt:O}";
        State[$"{CallbackPrefix}{callbackId}"] = new StateEntry($"{CallbackPrefix}{callbackId}", value);
        await WriteStateAsync(ct);
    }

    public override async Task<AgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct = default)
    {
        var stateKey = $"{CallbackPrefix}{callbackId}";
        if (!State.TryGetValue(stateKey, out var entry))
            return new AgentResponse([]);

        var parts = entry.Value.ToString()!.Split('|', 3);
        if (parts.Length < 3)
            return new AgentResponse([]);

        var grainType = parts[0];
        var grainId = parts[1];
        var expiresAt = DateTimeOffset.Parse(parts[2]);

        if (DateTimeOffset.UtcNow > expiresAt)
        {
            State.Remove(stateKey);
            await WriteStateAsync(ct);
            return new AgentResponse([]);
        }

        var targetGrainId = Orleans.Runtime.GrainId.Create(grainType, grainId);
        var targetAgent = GrainFactory.GetGrain<IAgent>(targetGrainId);
        return await targetAgent.HandleCallback(callbackId, value, ct);
    }

}
