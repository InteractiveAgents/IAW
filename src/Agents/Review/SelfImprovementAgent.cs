using IAW.Agents.Infrastructure;
using IAW.Agents.Messages;
using IAW.Core;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Orleans.Streams;
using System.ComponentModel;
using System.Text.Json;
using Core.Contracts;
using Core.AI;
using Core.AI.Models;
using Core.Communication;

namespace IAW.Agents.Review;

public class SelfImprovementAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient),
      ISelfImprovement,
      IReceiver<ReviewCompletedMessage>,
      IStreamConsumer<TestsPassedEvent>,
      IStreamConsumer<CodeChangedEvent>
{
    protected override string DisplayName => "Self-Improvement Agent";

    protected override string Instructions => """
        You are the Self-Improvement Agent. You observe the engineering team's performance metrics
        and propose concrete improvements to the codebase. You can:
        - Analyze build/test metrics to identify patterns (frequent failures, slow builds)
        - Read source files and identify code quality issues
        - Generate improvement proposals with specific file changes
        - Execute self-modification cycles: read code -> propose change -> apply -> build -> test -> commit

        Focus on high-impact improvements: reducing build warnings, improving test coverage,
        eliminating code duplication, and improving agent prompts. Always verify changes build
        and tests pass before committing.
        """;

    protected override AgentKind AgentKindValue => AgentKind.Static;

    public Task OnStreamEventAsync(TestsPassedEvent evt, StreamSequenceToken? token)
        => AccumulateTestMetrics(evt.Passed, evt.Failed);

    public Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
        => RecordCodeChange(evt.Author, "code.changed", DateTimeOffset.UtcNow);

    private async Task AccumulateTestMetrics(int passed, int failed)
    {
        var currentPassed = GetIntFromState("total-tests-passed");
        var currentFailed = GetIntFromState("total-tests-failed");

        State["total-tests-passed"] = new StateEntry("total-tests-passed", (currentPassed + passed).ToString());
        State["total-tests-failed"] = new StateEntry("total-tests-failed", (currentFailed + failed).ToString());
        await WriteStateAsync(default);
    }

    private async Task RecordCodeChange(string source, string eventName, DateTimeOffset timestamp)
    {
        var changeCount = GetIntFromState("code-changes-count");

        State["code-changes-count"] = new StateEntry("code-changes-count", (changeCount + 1).ToString());
        State[$"change-{changeCount + 1}"] = new StateEntry($"change-{changeCount + 1}",
            JsonSerializer.Serialize(new
            {
                Source = source,
                EventName = eventName,
                Timestamp = timestamp
            }));
        await WriteStateAsync(default);
    }

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(AnalyzeCodeQuality, nameof(AnalyzeCodeQuality),
                "Analyze a source file for quality issues and improvement opportunities"),
            AIFunctionFactory.Create(CollectBuildMetrics, nameof(CollectBuildMetrics),
                "Collect current build and test metrics from the Build agent"),
            AIFunctionFactory.Create(ProposeImprovement, nameof(ProposeImprovement),
                "Create an improvement proposal for a specific file"),
            AIFunctionFactory.Create(ExecuteSelfModification, nameof(ExecuteSelfModification),
                "Execute a self-modification cycle: modify code, build, test, commit"),
            AIFunctionFactory.Create(GetAllProposals, nameof(GetAllProposals),
                "Get all pending improvement proposals"),
        ];
    }

    [Description("Analyze a source file for code quality issues")]
    private async Task<string> AnalyzeCodeQuality(
        [Description("Path to the source file to analyze")] string filePath)
    {
        var fileAgent = GrainFactory.GetGrain<IFileSystem>("fs");
        var content = await fileAgent.ReadFileAsync(filePath);

        var prompt = $"""
            Analyze this C# file for improvement opportunities.
            Categories: naming, duplication, complexity, missing-tests, performance, prompts.
            Be specific -- reference line numbers and suggest concrete changes.

            File: {filePath}
            Content:
            {content}
            """;

        var chatHistory = new List<AIChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, prompt)
        };
        try
        {
            var response = await ChatClient.GetResponseAsync(chatHistory);
            return response.Text ?? "No analysis available";
        }
        catch (Exception ex)
        {
            return $"Analysis failed: {ex.GetBaseException().Message}";
        }
    }

    [Description("Collect current build and test metrics")]
    private async Task<string> CollectBuildMetrics()
    {
        var buildAgent = GrainFactory.GetGrain<IBuild>("build");
        var metrics = await buildAgent.GetMetricsAsync();

        var testsPassed = GetIntFromState("total-tests-passed");
        var testsFailed = GetIntFromState("total-tests-failed");

        var summary = new
        {
            metrics.TotalBuilds,
            metrics.FailedBuilds,
            BuildSuccessRate = metrics.TotalBuilds > 0
                ? (metrics.TotalBuilds - metrics.FailedBuilds) * 100.0 / metrics.TotalBuilds
                : 100.0,
            AverageBuildTimeMs = metrics.AverageBuildTime.TotalMilliseconds,
            metrics.TotalWarnings,
            metrics.TotalErrors,
            TestsPassed = testsPassed,
            TestsFailed = testsFailed,
            CodeChangesObserved = GetIntFromState("code-changes-count"),
            ReviewsCompleted = GetIntFromState("reviews-count")
        };

        await PublishAsync("metrics.collected", new Dictionary<string, object>
        {
            ["TotalBuilds"] = metrics.TotalBuilds,
            ["FailedBuilds"] = metrics.FailedBuilds,
            ["TotalWarnings"] = metrics.TotalWarnings,
            ["TestsPassed"] = testsPassed,
            ["TestsFailed"] = testsFailed
        });

        return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
    }

    [Description("Create an improvement proposal")]
    private async Task<string> ProposeImprovement(
        [Description("Target file path")] string targetFile,
        [Description("Description of the improvement")] string description,
        [Description("Category: naming, duplication, complexity, performance, prompts")] string category,
        [Description("Priority: high, medium, low")] string priority)
    {
        var proposalId = $"prop-{Guid.NewGuid():N}"[..12];
        var proposal = new ImprovementProposalMessage(proposalId, targetFile, description, category, priority,
            this.GetPrimaryKeyString(), Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);

        State[$"proposal-{proposalId}"] = new StateEntry($"proposal-{proposalId}",
            JsonSerializer.Serialize(proposal));
        await WriteStateAsync(default);

        await PublishAsync("improvement.proposed", new Dictionary<string, object>
        {
            ["ProposalId"] = proposalId,
            ["TargetFile"] = targetFile,
            ["Category"] = category,
            ["Priority"] = priority
        });

        return $"Improvement proposal created: {proposalId} ({category}/{priority})";
    }

    [Description("Execute a self-modification cycle on a target file")]
    private async Task<string> ExecuteSelfModification(
        [Description("File to modify")] string targetFile,
        [Description("Description of the change to make")] string changeDescription)
    {
        var fileAgent = GrainFactory.GetGrain<IFileSystem>("fs");
        var buildAgent = GrainFactory.GetGrain<IBuild>("build");
        var gitAgent = GrainFactory.GetGrain<IGit>("git");

        var originalContent = await fileAgent.ReadFileAsync(targetFile);
        var projectDir = Path.GetDirectoryName(targetFile) ?? "";

        var prompt = $"""
            Apply this improvement to the file. Return ONLY the complete modified file content, no explanations.

            Change: {changeDescription}
            File: {targetFile}
            Original content:
            {originalContent}
            """;

        var chatHistory = new List<AIChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, prompt)
        };
        string modifiedContent;
        try
        {
            var response = await ChatClient.GetResponseAsync(chatHistory);
            modifiedContent = response.Text ?? "";
        }
        catch (Exception ex)
        {
            return $"Failed: Could not generate modification content. {ex.GetBaseException().Message}";
        }

        if (string.IsNullOrWhiteSpace(modifiedContent))
            return "Failed: LLM returned empty content";

        await fileAgent.WriteFileAsync(targetFile, modifiedContent);

        var buildResult = await buildAgent.BuildAsync(projectDir);
        if (!buildResult.Success)
        {
            await fileAgent.WriteFileAsync(targetFile, originalContent);
            return $"Failed: Build errors after modification. Reverted. Errors: {string.Join("; ", buildResult.Diagnostics.Take(3))}";
        }

        var testResult = await buildAgent.TestAsync(projectDir);
        if (!testResult.Success)
        {
            await fileAgent.WriteFileAsync(targetFile, originalContent);
            return $"Failed: {testResult.Failed} test(s) failed after modification. Reverted.";
        }

        var repoPath = FindRepoRoot(targetFile);
        await gitAgent.CommitAsync(repoPath, $"self-improvement: {changeDescription}");

        State["last-self-modification"] = new StateEntry("last-self-modification",
            JsonSerializer.Serialize(new
            {
                File = targetFile,
                Change = changeDescription,
                Timestamp = DateTimeOffset.UtcNow,
                BuildWarnings = buildResult.Warnings,
                TestsPassed = testResult.Passed
            }));
        await WriteStateAsync(default);

        await PublishAsync("self.modified", new Dictionary<string, object>
        {
            ["File"] = targetFile,
            ["Change"] = changeDescription,
            ["BuildWarnings"] = buildResult.Warnings,
            ["TestsPassed"] = testResult.Passed
        });

        return $"Self-modification successful: {targetFile} modified, build OK ({buildResult.Warnings} warnings), {testResult.Passed} tests passed, committed.";
    }

    [Description("Get all pending improvement proposals")]
    private Task<string> GetAllProposals()
    {
        var proposals = State
            .Where(kvp => kvp.Key.StartsWith("proposal-"))
            .Select(kvp => kvp.Value.Value.ToString() ?? "")
            .ToArray();

        return Task.FromResult(proposals.Length > 0
            ? string.Join("\n", proposals)
            : "No pending proposals");
    }

    public async Task<MessageReceipt> ReceiveAsync(ReviewCompletedMessage message, CancellationToken ct = default)
    {
        var reviewCount = GetIntFromState("reviews-count");

        State["reviews-count"] = new StateEntry("reviews-count", (reviewCount + 1).ToString());
        State[$"review-{reviewCount + 1}"] = new StateEntry($"review-{reviewCount + 1}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
    }

    public Task<string[]> GetPendingProposalsAsync(CancellationToken ct = default)
    {
        var proposals = State
            .Where(kvp => kvp.Key.StartsWith("proposal-"))
            .Select(kvp => kvp.Value.Value.ToString() ?? "")
            .ToArray();
        return Task.FromResult(proposals);
    }

    public Task<string> GetMetricsSummaryAsync(CancellationToken ct = default)
        => CollectBuildMetrics();

    public async Task TriggerAnalysisAsync(CancellationToken ct = default)
    {
        var prompt = """
            Collect build metrics and analyze recent code changes. If you see patterns
            (frequent warnings, failing tests, code duplication), create improvement proposals.
            Focus on the highest-impact improvements first.
            """;

        var chatHistory = new List<AIChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, prompt)
        };
        try
        {
            await ChatClient.GetResponseAsync(chatHistory, cancellationToken: ct);
            State["last-analysis-status"] = new StateEntry("last-analysis-status", "ok");
        }
        catch (Exception ex)
        {
            State["last-analysis-status"] = new StateEntry("last-analysis-status", "failed");
            State["last-analysis-error"] = new StateEntry("last-analysis-error", ex.GetBaseException().Message);
        }

        await WriteStateAsync(ct);
    }

    Task<bool> IReceiver<ReviewCompletedMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    private int GetIntFromState(string key)
    {
        if (!State.TryGetValue(key, out var desc)) return 0;
        return int.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private static string FindRepoRoot(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.GetDirectoryName(filePath) ?? ".";
    }
}
