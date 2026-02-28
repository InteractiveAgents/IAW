using Orleans;

namespace Core;

public interface IAgent :
    IGrainWithStringKey,
    IAgentMetadataBehavior,
    IAgentStateBehavior,
    IAgentHistoryBehavior,
    IAgentEventsBehavior,
    IAgentNotificationsBehavior,
    IAgentTrackingBehavior,
    IAgentToolsBehavior,
    IAgentStreamsBehavior;
