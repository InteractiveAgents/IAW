namespace Core.Contracts.Events;

public static class AgentEventType
{
    // build
    public const string BuildSucceeded = "build.succeeded";
    public const string BuildFailed = "build.failed";

    // test
    public const string TestPassed = "test.passed";
    public const string TestFailed = "test.failed";

    // file
    public const string FileCreated = "file.created";
    public const string FileRead = "file.read";
    public const string FileWritten = "file.written";

    // git
    public const string CommitCreated = "commit.created";
    public const string RevertCompleted = "revert.completed";

    // orchestration
    public const string TaskCreated = "task.created";
    public const string TaskCompleted = "task.completed";
    public const string StepCompleted = "step.completed";
    public const string StepFailed = "step.failed";

    // validation
    public const string ValidationPassed = "validation.passed";
    public const string ValidationFailed = "validation.failed";

    // scheduling
    public const string JobCompleted = "job.completed";

    // system
    public const string HealthWarning = "health.warning";
    public const string HealthCritical = "health.critical";
    public const string ApprovalRequested = "approval.requested";
    public const string ApprovalResolved = "approval.resolved";
    public const string ToolAuthorizationRequested = "tool.authorization.requested";
    public const string ToolAuthorized = "tool.authorized";
    public const string ToolDenied = "tool.denied";

    // knowledge
    public const string DecisionRecorded = "decision.recorded";

    // deployment
    public const string DeploySucceeded = "deploy.succeeded";
    public const string DeployFailed = "deploy.failed";
}
