namespace Core;

public static class IAWConstants
{
    public const string StreamProvider = "agents";

    public static class Events
    {
        public const string ApprovalRequested = "approval.requested";
public const string DashboardChanged = "dashboard.changed";
        public const string JobCompleted = "job.completed";
        public const string OrchestrationProgress = "orchestration.progress";
        public const string OrchestrationCompleted = "orchestration.completed";
    }

    public static class GrainTypes
    {
        public const string Agent = "agent";
        public const string Thread = "thread";
        public const string CodeOrchestrator = "code-orchestrator";
        public const string UserProfile = "user-profile";
        public const string UISession = "ui-session";
        public const string AgentRegistry = "agent-registry";
    }

    public static class StateKeys
    {
        public const string SetupComplete = "setup-complete";
        public const string GroupChatId = "group-chat-id";
        public const string ScheduledDashboardMsgId = "scheduled-dashboard-msgid";
    }
}
