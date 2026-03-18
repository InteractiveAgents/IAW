namespace Core.Contracts;

[GenerateSerializer]
public record StateEntry(
    [property: Id(0)] string Key,
    [property: Id(1)] object Value);