namespace IAW.Testing.Scenario;

public sealed class StreamRef(string streamNamespace, Guid streamId)
{
    public string Namespace { get; } = streamNamespace;
    public Guid StreamId { get; } = streamId;
}
