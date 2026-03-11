namespace Core.Orchestration;

[GenerateSerializer]
public enum OrchestrationStatus
{
    Created,
    Running,
    Paused,
    Completed,
    Failed,
    Recovering
}

[GenerateSerializer]
public enum StepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}
