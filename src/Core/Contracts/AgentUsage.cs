namespace Core.Contracts;

[GenerateSerializer]
public record AgentUsage(
    [property: Id(0)] long InputTokens,
    [property: Id(1)] long OutputTokens,
    [property: Id(2)] long TotalTokens);
