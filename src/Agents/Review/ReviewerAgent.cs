using Core.AI;
using Core.AI.Models;
using Core.Communication;
using Core.Context;
using Core.Contracts;
using Core.Messages;
using IAW.Agents.Infrastructure;
using IAW.Agents.Memory;
using IAW.Core;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Orleans.Streams;

namespace IAW.Agents.Review;

public class ReviewerAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient),
      IReviewer,
      IStreamConsumer<CodeChangedEvent>
{
    protected override string DisplayName => "Reviewer";

    protected override IReadOnlyList<global::Core.Context.IAgentContextProvider> GetContextProviders() =>
    [
        new MemoryContextProvider([
            GrainFactory.GetGrain<IPatternMemory>("pattern-memory"),
            GrainFactory.GetGrain<IProjectMemory>("project-memory"),
        ])
    ];

    protected override string Instructions => """
        You are the Reviewer, the IAW team's code quality guardian. Review C# code for correctness, security, and pattern consistency.

        WHEN REVIEWING CODE:
        1. Check for: correctness, error handling, edge cases, naming consistency with project patterns
        2. Check for: security issues (path traversal, unvalidated input, hardcoded secrets, SQL injection)
        3. Check for: performance issues (N+1 patterns, unbounded loops, missing async operations)
        4. Check for: consistency with project conventions (naming, no XML docs, architectural patterns)

        REVIEW FORMAT:
        Start with a one-line verdict: APPROVE, NEEDS_CHANGES, or BLOCK
        List issues grouped by severity: CRITICAL > HIGH > MEDIUM > LOW
        For each issue: file path, line range, description, concrete fix suggestion
        End with what's done well in the code

        RULES:
        - Be specific: "Wrap the HTTP call on line 42 in try-catch for HttpRequestException" not "Consider error handling"
        - Don't flag style preferences — only bugs, security risks, or pattern violations
        - Skip praise and general observations; focus on actionable improvements
        """;

    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        State["last-review-files"] = new StateEntry(
            "last-review-files", string.Join("|", evt.ChangedFiles));
        await WriteStateAsync(default);

        var fileAgent = GrainFactory.GetGrain<IFileSystem>("fs");

        var reviewContent = new List<string>();
        foreach (var filePath in evt.ChangedFiles)
        {
            var content = await fileAgent.ReadFileAsync(filePath, default);
            reviewContent.Add($"// {filePath}\nContent:\n{content}");
        }

        var prompt = $"Review these C# code changes for quality:\n{string.Join("\n---\n", reviewContent)}";
        var chatHistory = new List<AIChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, prompt)
        };
        var response = await ChatClient.GetResponseAsync(chatHistory, cancellationToken: default);
        var reviewText = response.Text ?? "";

        var hasIssues = reviewText.Contains("issue", StringComparison.OrdinalIgnoreCase)
                     || reviewText.Contains("fix", StringComparison.OrdinalIgnoreCase)
                     || reviewText.Contains("problem", StringComparison.OrdinalIgnoreCase);

        if (hasIssues)
        {
            var taskId = State.TryGetValue("last-review-task-id", out var desc)
                ? desc.Value.ToString()!
                : Guid.NewGuid().ToString("N")[..8];

            await PublishAsync("review.feedback", new Dictionary<string, object>
            {
                ["TaskId"] = taskId,
                ["Feedback"] = reviewText,
                ["FilesAffected"] = evt.ChangedFiles
            }, default);
        }

        var reviewApproved = !hasIssues;
        await PublishAsync("review.completed", new Dictionary<string, object>
        {
            ["Approved"] = reviewApproved,
            ["Summary"] = reviewText
        }, default);
    }
}
