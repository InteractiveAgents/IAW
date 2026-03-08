using IAW.Core;

namespace IAW.Testing.Scenario;

public sealed class ScenarioBuilder(Func<string, IAgent> agentFactory)
{
    private readonly List<ScenarioStep> _steps = [];

    public AgentStepBuilder Given(AgentRef agent) => new(this, agent, StepPhase.Given);
    public AgentStepBuilder When(AgentRef agent) => new(this, agent, StepPhase.When);
    public AgentStepBuilder Then(AgentRef agent) => new(this, agent, StepPhase.Then);

    public AgentRef Agent(string id) => new(agentFactory, id);

    internal void AddStep(ScenarioStep step) => _steps.Add(step);

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        foreach (var step in _steps)
        {
            await step.ExecuteAsync(cts.Token);
        }
    }
}

public enum StepPhase { Given, When, Then }

public sealed class AgentStepBuilder(ScenarioBuilder scenario, AgentRef agent, StepPhase phase)
{
    public ScenarioBuilder Subscribes(string topic, string to)
    {
        scenario.AddStep(new GivenSubscribesStep(agent, topic, to));
        return scenario;
    }

    public ScenarioBuilder HasState(string key, string value)
    {
        if (phase == StepPhase.Then)
        {
            scenario.AddStep(new ThenHasStateStep(agent, key, value));
            return scenario;
        }

        scenario.AddStep(new GivenStateStep(agent, key, value));
        return scenario;
    }

    public ScenarioBuilder HasHistory(string role, string content)
    {
        scenario.AddStep(new GivenHistoryStep(agent, role, content));
        return scenario;
    }

    public ScenarioBuilder Notifies(string topic, string payload)
    {
        scenario.AddStep(new WhenNotifiesStep(agent, topic, payload));
        return scenario;
    }

    public ScenarioBuilder NotifiesWithEnvelope(NotificationEnvelope envelope)
    {
        scenario.AddStep(new WhenNotifiesEnvelopeStep(agent, envelope));
        return scenario;
    }

    public ScenarioBuilder PublishesEvent(string name, string? payload = null)
    {
        scenario.AddStep(new WhenPublishesEventStep(agent, name, payload));
        return scenario;
    }

    public ScenarioBuilder PublishesStream(string streamNamespace, Guid streamId, string message)
    {
        scenario.AddStep(new WhenPublishesStreamStep(agent, streamNamespace, streamId, message));
        return scenario;
    }

    public ScenarioBuilder SetsState(string key, string value)
    {
        scenario.AddStep(new WhenSetsStateStep(agent, key, value));
        return scenario;
    }

    public ScenarioBuilder Increments(string key)
    {
        scenario.AddStep(new WhenIncrementsStep(agent, key));
        return scenario;
    }

    public ScenarioBuilder AddsHistory(string role, string content)
    {
        scenario.AddStep(new WhenAddsHistoryStep(agent, role, content));
        return scenario;
    }

    public ScenarioBuilder HasNotification(string topic, string payload)
    {
        scenario.AddStep(new ThenHasNotificationStep(agent, topic, payload));
        return scenario;
    }

    public ScenarioBuilder HasNotificationMatching(Func<NotificationRecord, bool> predicate)
    {
        scenario.AddStep(new ThenHasNotificationMatchingStep(agent, predicate));
        return scenario;
    }

    public ScenarioBuilder HasEvent(string name)
    {
        scenario.AddStep(new ThenHasEventStep(agent, name));
        return scenario;
    }

    public ScenarioBuilder HasHistory(int count)
    {
        scenario.AddStep(new ThenHasHistoryCountStep(agent, count));
        return scenario;
    }

    public ScenarioBuilder HasTrackingStatus(Func<AgentTrackingStatus, bool> predicate)
    {
        scenario.AddStep(new ThenHasTrackingStatusStep(agent, predicate));
        return scenario;
    }
}
