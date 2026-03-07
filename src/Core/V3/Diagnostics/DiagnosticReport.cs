namespace Core.V3.Diagnostics;

[GenerateSerializer]
public record DiagnosticReport(
    [property: Id(0)] string AgentType,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] bool Healthy,
    [property: Id(3)] int TestsRun,
    [property: Id(4)] int TestsPassed,
    [property: Id(5)] TimeSpan Duration,
    [property: Id(6)] IReadOnlyList<DiagnosticFailure> Failures);

[GenerateSerializer]
public record DiagnosticFailure(
    [property: Id(0)] string TestName,
    [property: Id(1)] string Message,
    [property: Id(2)] string? StackTrace);
