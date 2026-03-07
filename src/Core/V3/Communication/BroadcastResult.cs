namespace Core.V3.Communication;

[GenerateSerializer]
public record BroadcastResult(
    [property: Id(0)] int TotalReceivers,
    [property: Id(1)] int Delivered,
    [property: Id(2)] int Failed,
    [property: Id(3)] string[] FailedReceiverIds);
