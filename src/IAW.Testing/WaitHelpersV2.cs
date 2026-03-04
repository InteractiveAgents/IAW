using Core;
using Core.V2;

namespace IAW.Testing;

public static class WaitHelpersV2
{
    public static async Task<ScheduleStatus> WaitForScheduleToStopAsync(
        IAgentV2 agent,
        CancellationToken ct = default)
    {
        return await WaitHelpers.WaitForAsync(
            () => agent.GetScheduleStatusAsync(ct),
            status => !status.IsRunning,
            timeout: TimeSpan.FromSeconds(5),
            ct: ct);
    }

    public static async Task<List<NotificationRecord>> WaitForNotificationsV2Async(
        IAgentV2 agent,
        int expectedCount,
        CancellationToken ct = default)
    {
        return await WaitHelpers.WaitForAsync(
            () => agent.QueryNotificationsAsync(ct),
            notifications => notifications.Count >= expectedCount,
            timeout: TimeSpan.FromSeconds(5),
            ct: ct);
    }
}
