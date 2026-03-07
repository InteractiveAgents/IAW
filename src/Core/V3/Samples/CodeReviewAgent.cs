using Core.V3.Communication;
using Core.V3.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace Core.V3.Samples;

public interface ICodeReviewAgent : IAgent;

public class CodeReviewAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      ICodeReviewAgent,
      IStreamConsumer<CodeChangedEvent>
{
    protected override string Instructions =>
        "You are a code review agent. When code changes arrive, analyze them for bugs, style issues, and security vulnerabilities.";

    protected override string DisplayName => "Code Review Bot";

    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        var fileList = string.Join(", ", evt.FilePaths);
        var prompt = $"Review these changed files: {fileList}. Commit: {evt.CommitSha}";
        await GetResponse(prompt, AgentCancellation);
    }
}
