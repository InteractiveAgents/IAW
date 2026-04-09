namespace Core.Contracts;

[GenerateSerializer]
public record PreferenceRule(
    [property: Id(0)] string Category,
    [property: Id(1)] string Rule,
    [property: Id(2)] string? Reason,
    [property: Id(3)] string Confidence,
    [property: Id(4)] DateTimeOffset CreatedAt)
{
    public static PreferenceRule Create(string category, string rule, string? reason, string confidence)
        => new(category, rule, reason, confidence, DateTimeOffset.UtcNow);
}
