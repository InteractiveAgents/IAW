using Core.Contracts;
using Core.Messages;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.Communication;

public class TypedPublishTests : AgentTest<ProducerTestAgent>
{
    [Fact]
    public async Task PublishToStream_LogsEventWithCorrectStreamName()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IProducerTestAgent>(UniqueId("typed-log"));
        var evt = new CodeChangedEvent(["file.cs"], "test", "test change", "agent-1", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        await grain.PublishCodeChanged(evt, ct);

        var log = await ((IAgent)grain).GetEventLog(ct);
        Assert.Single(log);
        Assert.Equal("code.changed", log[0].EventName);
    }

    [Fact]
    public async Task PublishToStream_PreservesSourceAndCorrelation()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IProducerTestAgent>(UniqueId("typed-src"));
        var correlationId = Guid.NewGuid().ToString();
        var evt = new CodeChangedEvent(["a.cs", "b.cs"], "test", "test change", "my-agent", correlationId, DateTimeOffset.UtcNow);
        await grain.PublishCodeChanged(evt, ct);

        var log = await ((IAgent)grain).GetEventLog(ct);
        Assert.Equal("my-agent", log[0].SourceAgentId);
        Assert.Equal(correlationId, log[0].CorrelationId);
    }

    [Fact]
    public async Task PublishToStream_MultipleEvents_AllLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IProducerTestAgent>(UniqueId("typed-multi"));

        for (var i = 0; i < 3; i++)
        {
            var evt = new CodeChangedEvent([$"file{i}.cs"], "test", "test change", "agent", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
            await grain.PublishCodeChanged(evt, ct);
        }

        var log = await ((IAgent)grain).GetEventLog(ct);
        Assert.Equal(3, log.Count);
        Assert.All(log, entry => Assert.Equal("code.changed", entry.EventName));
    }

    [Fact]
    public void EventTypeToStreamName_RemovesEventSuffix()
    {
        Assert.Equal("code.changed", IAW.Core.Agent.EventTypeToStreamName(typeof(CodeChangedEvent)));
    }

    [Fact]
    public void EventTypeToStreamName_HandlesMultiWordNames()
    {
        Assert.Equal("step.progress", IAW.Core.Agent.EventTypeToStreamName(typeof(global::Core.Messages.Events.StepProgressEvent)));
    }
}
