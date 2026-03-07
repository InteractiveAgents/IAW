namespace IAW.Core.Communication;

[GenerateSerializer]
public record MessageReceipt(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] string ReceiptId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string? RejectionReason);
