using Core;
using Core.V2;
using Orleans.Journaling;

namespace IAW.Core.Tests;

public interface ITestAgentV2 : IAgentV2;

public sealed class TestAgentV2(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking)
    : AgentV2(messages, memory, events, subscriptions, notifications, tracking), ITestAgentV2
{
    protected override IAgentV2 ResolveSubscriber(string subscriberId)
        => GrainFactory.GetGrain<ITestAgentV2>(subscriberId);

    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Test Agent V2",
        Description = "A test agent for V2 behavior tests",
        Capabilities = ["memory", "messages", "events", "notifications", "scheduling", "streams", "tools"]
    };

    protected override Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct)
        => Task.FromResult(new AgentReply { Output = $"Echo: {request.Input}" });
}
