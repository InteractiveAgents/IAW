using Orleans.Journaling;

namespace Core.Contracts;

public sealed class AgentDurableState(
    IDurableDictionary<string, StateEntry> state,
    IDurableList<AgentEvent> eventLog,
    IDurableList<ChatMessage> history,
    IDurableDictionary<string, TrackingItem> trackingItems)
{
    public IDurableDictionary<string, StateEntry> State => state;
    public IDurableList<AgentEvent> EventLog => eventLog;
    public IDurableList<ChatMessage> History => history;
    public IDurableDictionary<string, TrackingItem> TrackingItems => trackingItems;
}
