namespace IAW.Core.Communication.Messages;

[GenerateSerializer]
public record TestResultMessage(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] int Total,
    [property: Id(2)] int Passed,
    [property: Id(3)] int Failed);
