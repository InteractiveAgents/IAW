namespace IAW.Core.Communication.Messages;

[GenerateSerializer]
public record AgentProgressUpdate(
    [property: Id(0)] string AgentId,
    [property: Id(1)] string Step,
    [property: Id(2)] string Status,
    [property: Id(3)] float? Progress = null);
