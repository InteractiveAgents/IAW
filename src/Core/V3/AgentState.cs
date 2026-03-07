namespace Core.V3;

[GenerateSerializer]
public record AgentState(
    [property: Id(0)] Dictionary<string, StateEntry> Entries);
