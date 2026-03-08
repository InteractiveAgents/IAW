using IAW.Core;

namespace IAW.Testing;

public static class WaitHelpers
{
    public static async Task<T> WaitForAsync<T>(
        Func<Task<T>> query,
        Func<T, bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken ct = default)
    {
        var totalTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(25);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(totalTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var result = await query();
            if (condition(result))
                return result;

            await Task.Delay(interval, cts.Token);
        }

        throw new TimeoutException($"Condition not met within {totalTimeout.TotalSeconds}s.");
    }

    public static async Task<AgentTrackingStatus> WaitForTrackingToStopAsync(
        IAgent agent,
        CancellationToken ct = default)
    {
        return await WaitForAsync(
            () => agent.GetTrackingStatusAsync(ct),
            status => !status.IsTracking,
            timeout: TimeSpan.FromSeconds(5),
            ct: ct);
    }

    public static async Task<List<NotificationRecord>> WaitForNotificationsAsync(
        IAgent agent,
        int expectedCount,
        CancellationToken ct = default)
    {
        return await WaitForAsync(
            () => agent.GetNotificationsAsync(ct),
            notifications => notifications.Count >= expectedCount,
            timeout: TimeSpan.FromSeconds(5),
            ct: ct);
    }
}
