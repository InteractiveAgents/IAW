using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Core;

internal static class AgentObservability
{
    public static readonly ActivitySource ActivitySource = new("Core.Agent");

    private static readonly Meter Meter = new("Core.Agent");
    private static long _sendCount;
    private static long _toolCallCount;
    private static long _failureCount;

    private static readonly ObservableCounter<long> SendCounter = Meter.CreateObservableCounter(
        "core.agent.sends",
        () => Volatile.Read(ref _sendCount));

    private static readonly ObservableCounter<long> ToolCallCounter = Meter.CreateObservableCounter(
        "core.agent.tool_calls",
        () => Volatile.Read(ref _toolCallCount));

    private static readonly ObservableCounter<long> FailureCounter = Meter.CreateObservableCounter(
        "core.agent.failures",
        () => Volatile.Read(ref _failureCount));

    public static void RecordSend() => Interlocked.Increment(ref _sendCount);
    public static void RecordToolCall() => Interlocked.Increment(ref _toolCallCount);
    public static void RecordFailure() => Interlocked.Increment(ref _failureCount);

    public static IReadOnlyDictionary<string, long> GetSnapshot() => new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        ["sends"] = Volatile.Read(ref _sendCount),
        ["toolCalls"] = Volatile.Read(ref _toolCallCount),
        ["failures"] = Volatile.Read(ref _failureCount)
    };
}

public sealed record AgentDiagnostics(
    bool IsHealthy,
    DateTimeOffset GeneratedAt,
    IReadOnlyDictionary<string, long> Counters,
    IReadOnlyDictionary<string, long> GlobalCounters,
    IReadOnlyList<string> RecentFailures,
    string? LastSendTraceId,
    string? LastSendSpanId);
