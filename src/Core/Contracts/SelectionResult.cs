namespace Core.Contracts;

[GenerateSerializer]
public record SelectionResult(
    [property: Id(0)] SelectionStatus Status,
    [property: Id(1)] List<string> SelectedAgents,
    [property: Id(2)] List<string> SuccessCriteria,
    [property: Id(3)] string? Plan,
    [property: Id(4)] List<ClarificationQuestion>? Questions);

[GenerateSerializer]
public record ClarificationQuestion(
    [property: Id(0)] string Text,
    [property: Id(1)] List<string>? Options);

[GenerateSerializer]
public enum SelectionStatus { Ready, NeedsClarification, CannotHandle }