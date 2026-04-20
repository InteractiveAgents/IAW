using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Core.Observability;

public static class AgentTelemetry
{
    public const string SourceName = "IAW";
    public const string MeterName = "IAW";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    // Agent lifecycle
    public static readonly Counter<long> Activations = Meter.CreateCounter<long>(
        "agents.activations", "{activation}", "Agent activations");
    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        "agents.messages.sent", "{message}", "Messages processed by agents");
    public static readonly Counter<long> ConversationErrors = Meter.CreateCounter<long>(
        "agents.conversations.errors", "{error}", "Conversation errors");
    public static readonly Histogram<double> ConversationDuration = Meter.CreateHistogram<double>(
        "agents.conversations.duration", "s", "Agent conversation turn duration");

    // Events
    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "agents.events.published", "{event}", "Events published by agents");
    public static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>(
        "agents.events.handled", "{event}", "Events handled by agents");
    public static readonly Histogram<double> EventHandleDuration = Meter.CreateHistogram<double>(
        "agents.events.handle_duration", "s", "Event handling duration");

    // GenAI token usage (OpenTelemetry semantic conventions v1.40)
    public static readonly Histogram<long> TokenUsage = Meter.CreateHistogram<long>(
        "gen_ai.client.token.usage", "{token}", "Token usage per LLM call");
    public static readonly Counter<long> TotalInputTokens = Meter.CreateCounter<long>(
        "agents.tokens.input", "{token}", "Cumulative input tokens across all agents");
    public static readonly Counter<long> TotalOutputTokens = Meter.CreateCounter<long>(
        "agents.tokens.output", "{token}", "Cumulative output tokens across all agents");

    // Approver (tool authorization) pipeline
    public static readonly Counter<long> ApproverFailures = Meter.CreateCounter<long>(
        "agents.approver.failures", "{failure}", "Approver errors treated as fail-closed deny");
    public static readonly Counter<long> ApproverDenies = Meter.CreateCounter<long>(
        "agents.approver.denies", "{deny}", "Tool calls denied by the Approver");
    public static readonly Counter<long> ApproverMemoHits = Meter.CreateCounter<long>(
        "agents.approver.memo_hits", "{hit}", "Approver decisions served from the memo table");
    public static readonly Counter<long> ApproverLlmJudgments = Meter.CreateCounter<long>(
        "agents.approver.llm_judgments", "{judgment}", "Approver judgments that invoked the LLM");
}