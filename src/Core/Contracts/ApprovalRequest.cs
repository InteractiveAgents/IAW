namespace Core.Contracts;

[GenerateSerializer]
public record ApprovalRequest(
    [property: Id(0)] string Id,
    [property: Id(1)] string Question,
    [property: Id(2)] IReadOnlyList<string> Options,
    [property: Id(3)] string RequestedBy,
    [property: Id(4)] DateTimeOffset Timestamp = default)
{
    public ApprovalRequest(string id, string question, IReadOnlyList<string> options, string requestedBy)
        : this(id, question, options, requestedBy, DateTimeOffset.UtcNow) { }
}

[GenerateSerializer]
public record ApprovalDecision(
    [property: Id(0)] string Choice,
    [property: Id(1)] string? Notes = null,
    [property: Id(2)] DateTimeOffset Timestamp = default)
{
    public ApprovalDecision(string choice, string? notes = null)
        : this(choice, notes, DateTimeOffset.UtcNow) { }
}
