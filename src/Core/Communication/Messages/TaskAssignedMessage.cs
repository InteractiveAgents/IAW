namespace IAW.Core.Communication.Messages;

[GenerateSerializer]
public record TaskAssignedMessage(
    [property: Id(0)] string FilePath,
    [property: Id(1)] string Description);
