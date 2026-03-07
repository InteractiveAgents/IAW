namespace IAW.Core.Communication.Messages;

[GenerateSerializer]
public record CodeChangedMessage(
    [property: Id(0)] string ProjectPath,
    [property: Id(1)] string FilePath,
    [property: Id(2)] string Description);
