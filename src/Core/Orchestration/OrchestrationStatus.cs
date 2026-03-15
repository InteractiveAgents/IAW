namespace Core.Orchestration;

[GenerateSerializer]
public enum OrchestrationStatus
{
    Created,
    Running,
    Paused,
    Completed,
    Failed,
    Recovering,
    SelfHealing
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
