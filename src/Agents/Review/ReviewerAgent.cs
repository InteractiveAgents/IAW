using IAW.Agents.Infrastructure;
using IAW.Agents.Messages;
using IAW.Core;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Orleans.Journaling;
using Orleans.Streams;
using Core.Contracts;
using Core.AI;
using Core.AI.Models;
using Core.Communication;

namespace IAW.Agents.Review;

public class ReviewerAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Sonnet46>] IChatClient chatClient,
    [Memory("history")] IDurableList<global::Core.Contracts.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IReviewer,
      IStreamConsumer<CodeChangedEvent>
{
    protected override string DisplayName => "Code Reviewer Agent";

    protected override string Instructions =>
        "You review C# code for quality, naming conventions, unnecessary comments, and architectural patterns. " +
        "Provide actionable feedback.";

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
