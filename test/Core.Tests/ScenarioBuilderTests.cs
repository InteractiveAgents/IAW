using Core;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public sealed class ScenarioBuilderTests : AgentTest<Agent>
{
    [Fact]
    public async Task Scenario_NotificationDelivery()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scenario
            .Given(Scenario.Agent("pub-s1")).Subscribes("alert", to: "sub-s1")
            .When(Scenario.Agent("pub-s1")).Notifies("alert", "fire")
            .Then(Scenario.Agent("sub-s1")).HasNotification("alert", "fire")
            .RunAsync(ct);
    }

    [Fact]
    public async Task Scenario_StateManipulation()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scenario
            .When(Scenario.Agent("counter-s1")).SetsState("city", "Seattle")
            .When(Scenario.Agent("counter-s1")).Increments("visits")
            .When(Scenario.Agent("counter-s1")).Increments("visits")
            .Then(Scenario.Agent("counter-s1")).HasState("city", "Seattle")
            .Then(Scenario.Agent("counter-s1")).HasState("visits", "2")
            .RunAsync(ct);
    }

    [Fact]
    public async Task Scenario_MultiAgentNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scenario
            .Given(Scenario.Agent("hub-s1")).Subscribes("alert", to: "a-s1")
            .Given(Scenario.Agent("hub-s1")).Subscribes("alert", to: "b-s1")
            .When(Scenario.Agent("hub-s1")).Notifies("alert", "fire")
            .Then(Scenario.Agent("a-s1")).HasNotification("alert", "fire")
            .Then(Scenario.Agent("b-s1")).HasNotification("alert", "fire")
            .RunAsync(ct);
    }

    [Fact]
    public async Task Scenario_EventPublishing()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scenario
            .When(Scenario.Agent("evt-s1")).PublishesEvent("test.event", "data")
            .Then(Scenario.Agent("evt-s1")).HasEvent("test.event")
            .RunAsync(ct);
    }

    [Fact]
    public async Task Scenario_EnvelopeNotification()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scenario
            .Given(Scenario.Agent("pub-env-s1")).Subscribes("weather", to: "sub-env-s1")
            .When(Scenario.Agent("pub-env-s1")).NotifiesWithEnvelope(new NotificationEnvelope
            {
                Topic = "weather",
                Payload = "{\"city\":\"Seattle\"}",
                Schema = "weather",
                SchemaVersion = "1.0"
            })
            .Then(Scenario.Agent("sub-env-s1")).HasNotificationMatching(n =>
                n.Schema == "weather" && n.SchemaVersion == "1.0")
            .RunAsync(ct);
    }

    [Fact]
    public async Task Scenario_HistoryTracking()
    {
        var ct = TestContext.Current.CancellationToken;
        await Scenario
            .When(Scenario.Agent("hist-s1")).AddsHistory("user", "hello")
            .When(Scenario.Agent("hist-s1")).AddsHistory("assistant", "hi")
            .Then(Scenario.Agent("hist-s1")).HasHistory(2)
            .RunAsync(ct);
    }
}
