using Orleans.Journaling;

namespace IAW.Core.Tests.V3;

public interface ITestAgentV3 : global::Core.V3.IAgent;

public class TestAgentV3(
    [global::Core.Memory("agent-state")] IDurableDictionary<string, global::Core.V3.StateEntry> state,
    [global::Core.Memory("agent-events")] IDurableList<global::Core.V3.AgentEvent> eventLog,
    Microsoft.Extensions.AI.IChatClient chatClient,
    [global::Core.Memory("v3-history")] IDurableList<global::Core.V3.ChatMessage> history,
    [global::Core.Memory("v3-tracking")] IDurableDictionary<string, global::Core.V3.TrackingItem> trackingItems)
    : global::Core.V3.Agent(state, eventLog, chatClient, history, trackingItems), ITestAgentV3
{
    protected override string Instructions => "You are a test agent.";
    protected override string DisplayName => "Test Agent V3";
}
