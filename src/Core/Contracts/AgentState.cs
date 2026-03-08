namespace IAW.Core;

[GenerateSerializer]
public record AgentState(
    [property: Id(0)] Dictionary<string, StateEntry> Entries);
