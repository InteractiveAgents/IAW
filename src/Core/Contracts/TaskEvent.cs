namespace Core.Contracts;

[GenerateSerializer]
public record TaskEvent(
    [property: Id(0)] string Agent,
    [property: Id(1)] string Action,
    [property: Id(2)] string Result,
    [property: Id(3)] string? Detail,
    [property: Id(4)] DateTimeOffset Timestamp)
{
    public string ToContextLine()
    {
        var detail = Detail is not null ? $" ({Detail})" : "";
        return $"- {Agent}: {Result}{detail}";
    }
}
