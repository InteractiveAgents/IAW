using IAW.Core;

namespace IAW.Testing.Scenario;

public abstract class ScenarioStep
{
    public abstract Task ExecuteAsync(CancellationToken ct);
}

public sealed class GivenSubscribesStep(AgentRef publisher, string topic, string subscriberId) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await publisher.Resolve().SubscribeAsync(topic, subscriberId, ct);
    }
}

public sealed class GivenStateStep(AgentRef agent, string key, string value) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().SetStateAsync(key, value, ct);
    }
}

public sealed class GivenHistoryStep(AgentRef agent, string role, string content) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().AddHistoryAsync(role, content, ct);
    }
}

public sealed class WhenNotifiesStep(AgentRef agent, string topic, string payload) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().NotifyAsync(topic, payload, ct);
    }
}

public sealed class WhenNotifiesEnvelopeStep(AgentRef agent, NotificationEnvelope envelope) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().NotifyAsync(envelope, ct);
    }
}

public sealed class WhenPublishesEventStep(AgentRef agent, string name, string? payload) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().PublishEventAsync(name, payload, ct);
    }
}

public sealed class WhenPublishesStreamStep(AgentRef agent, string streamNamespace, Guid streamId, string message) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().PublishStreamAsync(streamNamespace, streamId, message, ct);
    }
}

public sealed class WhenSetsStateStep(AgentRef agent, string key, string value) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().SetStateAsync(key, value, ct);
    }
}

public sealed class WhenIncrementsStep(AgentRef agent, string key) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().IncrementAsync(key, ct);
    }
}

public sealed class WhenAddsHistoryStep(AgentRef agent, string role, string content) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().AddHistoryAsync(role, content, ct);
    }
}

public sealed class ThenHasNotificationStep(AgentRef agent, string topic, string payload) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var notifications = await WaitHelpers.WaitForNotificationsAsync(agent.Resolve(), 1, ct);
        var match = notifications.Find(n => n.Topic == topic && n.Payload == payload);
        if (match is null)
            throw new Xunit.Sdk.XunitException(
                $"Agent '{agent.AgentId}' has no notification with topic='{topic}' payload='{payload}'. " +
                $"Found {notifications.Count} notification(s): [{string.Join(", ", notifications.Select(n => $"{{topic={n.Topic}, payload={n.Payload}}}"))}]");
    }
}

public sealed class ThenHasNotificationMatchingStep(AgentRef agent, Func<NotificationRecord, bool> predicate) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await WaitHelpers.WaitForAsync(
            () => agent.Resolve().GetNotificationsAsync(ct),
            list => list.Exists(n => predicate(n)),
            ct: ct);
    }
}

public sealed class ThenHasEventStep(AgentRef agent, string name) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await WaitHelpers.WaitForAsync(
            () => agent.Resolve().GetEventsAsync(ct),
            list => list.Exists(e => e.Name == name),
            ct: ct);
    }
}

public sealed class ThenHasStateStep(AgentRef agent, string key, string expectedValue) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var value = await agent.Resolve().GetStateValueAsync(key, ct);
        if (!string.Equals(value, expectedValue, StringComparison.Ordinal))
            throw new Xunit.Sdk.XunitException(
                $"Agent '{agent.AgentId}' state['{key}'] = '{value ?? "<null>"}', expected '{expectedValue}'.");
    }
}

public sealed class ThenHasHistoryCountStep(AgentRef agent, int expectedCount) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var history = await agent.Resolve().GetHistoryAsync(ct);
        if (history.Count != expectedCount)
            throw new Xunit.Sdk.XunitException(
                $"Agent '{agent.AgentId}' history count = {history.Count}, expected {expectedCount}.");
    }
}

public sealed class ThenHasTrackingStatusStep(AgentRef agent, Func<AgentTrackingStatus, bool> predicate) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await WaitHelpers.WaitForAsync(
            () => agent.Resolve().GetTrackingStatusAsync(ct),
            predicate,
            ct: ct);
    }
}
