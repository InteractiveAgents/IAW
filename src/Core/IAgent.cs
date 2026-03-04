using Core.V2;

namespace Core;

public interface IAgent :
    IAgentV2,
    IAgentMetadataBehavior,
    IAgentStateBehavior,
    IAgentHistoryBehavior,
    IAgentEventsBehavior,
    IAgentNotificationsBehavior,
    IAgentTrackingBehavior,
    IAgentToolsBehavior,
    IAgentStreamsBehavior;
