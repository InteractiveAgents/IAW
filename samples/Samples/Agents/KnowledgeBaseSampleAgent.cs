using System.ComponentModel;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Samples.Agents;

public interface IKnowledgeBaseSampleAgent : IAgent;

public class KnowledgeBaseSampleAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IKnowledgeBaseSampleAgent
{
    protected override string Instructions =>
        "You are a knowledge base agent. Answer questions using the indexed documents available through your tools.";

    protected override string DisplayName => "Knowledge Base";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(SearchDocuments),
        AIFunctionFactory.Create(GetDocument)
    ];

    [Description("Search indexed documents by keyword")]
    static string[] SearchDocuments([Description("Search query")] string query) =>
        [$"doc-1: Getting Started with IAW", $"doc-2: Agent Behaviors Guide"];

    [Description("Get a document by ID")]
    static string GetDocument([Description("Document ID")] string documentId) =>
        $"Document {documentId}: This is a sample document about IAW agent behaviors.";
}
