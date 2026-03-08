using Orleans.Journaling;

namespace IAW.Core.Tests;

public interface ITestAgent : global::IAW.Core.IAgent;

public class TestAgent(
    [global::IAW.Core.Memory("agent-state")] IDurableDictionary<string, global::IAW.Core.StateEntry> state,
    [global::IAW.Core.Memory("agent-events")] IDurableList<global::IAW.Core.AgentEvent> eventLog,
    Microsoft.Extensions.AI.IChatClient chatClient,
    [global::IAW.Core.Memory("v3-history")] IDurableList<global::IAW.Core.ChatMessage> history,
    [global::IAW.Core.Memory("v3-tracking")] IDurableDictionary<string, global::IAW.Core.TrackingItem> trackingItems)
    : global::IAW.Core.Agent(state, eventLog, chatClient, history, trackingItems), ITestAgent
{
    protected override string Instructions => "You are a test agent.";
    protected override string DisplayName => "Test Agent V3";
}
