using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IAW.Core.Observability;

public static class AgentTelemetry
{
    public const string SourceName = "IAW";
    public const string MeterName = "IAW";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "agents.events.published", "{event}", "Events published by agents");
    public static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>(
        "agents.events.handled", "{event}", "Events handled by agents");
    public static readonly Counter<long> Activations = Meter.CreateCounter<long>(
        "agents.activations", "{activation}", "Agent activations");
    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        "agents.messages.sent", "{message}", "Messages processed by agents");
    public static readonly Counter<long> ConversationErrors = Meter.CreateCounter<long>(
        "agents.conversations.errors", "{error}", "Conversation errors");
    public static readonly Histogram<double> EventHandleDuration = Meter.CreateHistogram<double>(
        "agents.events.handle_duration", "s", "Event handling duration");
    public static readonly Histogram<double> ConversationDuration = Meter.CreateHistogram<double>(
        "agents.conversations.duration", "s", "Conversation turn duration");
}
