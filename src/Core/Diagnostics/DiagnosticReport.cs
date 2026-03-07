namespace IAW.Core.Diagnostics;

[GenerateSerializer]
public record DiagnosticReport(
    [property: Id(0)] string AgentName,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] bool IsHealthy,
    [property: Id(3)] int EventCount,
    [property: Id(4)] int MessageCount,
    [property: Id(5)] TimeSpan Uptime,
    [property: Id(6)] string[] Issues);
