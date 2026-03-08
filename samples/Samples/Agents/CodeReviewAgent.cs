using IAW.Core;
using IAW.Core.Communication;
using IAW.Core.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace IAW.Samples.Agents;

public interface ICodeReviewAgent : IAgent;

public class CodeReviewAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
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
