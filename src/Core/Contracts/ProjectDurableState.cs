using Orleans.Journaling;

namespace Core.Contracts;

public class ProjectDurableState(
    IDurableDictionary<string, StateEntry> state,
    IDurableList<AgentEvent> eventLog,
    IDurableList<ChatMessage> history,
    IDurableDictionary<string, TrackingItem> trackingItems,
    IDurableList<ProjectTask> tasks,
    IDurableDictionary<string, ScheduledJob> schedules,
    IDurableDictionary<string, FileReference> files,
    IDurableDictionary<string, string> projectMeta)
    : AgentDurableState(state, eventLog, history, trackingItems)
{
    public IDurableList<ProjectTask> Tasks => tasks;
    public IDurableDictionary<string, ScheduledJob> Schedules => schedules;
    public IDurableDictionary<string, FileReference> Files => files;
    public IDurableDictionary<string, string> ProjectMeta => projectMeta;
}
