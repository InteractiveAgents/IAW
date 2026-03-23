using Orleans.Journaling;

namespace Core.Contracts;

public class AgentDurableState(
    IDurableDictionary<string, StateEntry> state,
    IDurableList<AgentEvent> eventLog,
    IDurableList<ChatMessage> history,
    IDurableDictionary<string, ScheduledJobItem> scheduledJobs)
{
    public IDurableDictionary<string, StateEntry> State => state;
    public IDurableList<AgentEvent> EventLog => eventLog;
    public IDurableList<ChatMessage> History => history;
    public IDurableDictionary<string, ScheduledJobItem> ScheduledJobs => scheduledJobs;
}
