namespace Core;

public static class IAWConstants
{
    public const string StreamProvider = "agents";
    public const string DelegationPrefix = "[DELEGATE]";
    public const string UIBroadcastProvider = "ui-notifications";

    public static class Events
    {
        public const string ApprovalRequested = "approval.requested";
        public const string ApprovalResolved = "approval.resolved";
        public const string DashboardChanged = "dashboard.changed";
        public const string JobCompleted = "job.completed";
        public const string OrchestrationProgress = "orchestration.progress";
        public const string ToolAuthorizationRequested = "tool.authorization.requested";
        public const string ToolAuthorized = "tool.authorized";
        public const string ToolDenied = "tool.denied";
    }

    public static class GrainTypes
    {
        public const string Agent = "agent";
        public const string Thread = "thread";
        public const string CodeOrchestrator = "code-orchestrator";
        public const string UserProfile = "user-profile";
        public const string UISession = "ui-session";
        public const string AgentRegistry = "agent-registry";
        public const string TaskLedger = "task-ledger";
        public const string EventRouter = "event-router";
        public const string Approver = "approver";
        public const string Preference = "preference";
    }

    public static class StateKeys
    {
        public const string SetupComplete = "setup-complete";
        public const string GroupChatId = "group-chat-id";
        public const string ScheduledDashboardMsgId = "scheduled-dashboard-msgid";
    }

    public static class PayloadKeys
    {
        public const string ProjectKey = "projectKey";
        public const string JobName = "jobName";
        public const string Result = "result";
        public const string TaskId = "taskId";
        public const string Phase = "phase";
        public const string Message = "message";
        public const string ApprovalId = "approvalId";
        public const string UserId = "userId";
        public const string Question = "question";
        public const string OptionsJson = "optionsJson";
        public const string DecisionKey = "decisionKey";
        public const string AgentId = "agentId";
        public const string ToolName = "toolName";
        public const string Reason = "reason";
    }
}