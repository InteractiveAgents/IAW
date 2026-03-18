using System.ComponentModel;
using System.Text;
using Core;
using Core.Contracts;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Core;

public abstract partial class Agent : IRemindable
{
    protected IDurableDictionary<string, TrackingItem> TrackingItems => durableState.TrackingItems;

    public async Task StartTrackingAsync(string name, TrackingItem item, TimeSpan interval, CancellationToken ct = default)
    {
        durableState.TrackingItems[name] = item;
        await WriteStateAsync(ct);
        await this.RegisterOrUpdateReminder(name, TimeSpan.Zero, interval);
    }

    public async Task StopTrackingAsync(string name, CancellationToken ct = default)
    {
        durableState.TrackingItems.Remove(name);
        await WriteStateAsync(ct);
        var reminder = await this.GetReminder(name);
        if (reminder is not null)
            await this.UnregisterReminder(reminder);
    }

    public virtual async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (durableState.TrackingItems.TryGetValue(reminderName, out var item))
        {
            var updated = item with { LastCheckAt = DateTimeOffset.UtcNow };
            durableState.TrackingItems[reminderName] = updated;
            await OnTrackingDueAsync(updated, AgentCancellation);
            await WriteStateAsync(AgentCancellation);
        }
    }

    protected virtual async Task OnTrackingDueAsync(TrackingItem item, CancellationToken ct)
    {
        var prompt = $"Check on this tracking item and report: {item.Description}";
        var chatHistory = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, prompt)
        };
        var tools = DefineTools();
        var options = tools.Count > 0 ? new ChatOptions { Tools = [.. tools] } : null;
        string result;
        try
        {
            var response = await ChatClient.GetResponseAsync(chatHistory, options, ct);
            result = response.Text ?? "";
        }
        catch (Exception ex)
        {
            result = BuildSafeErrorMessage(ex);
        }

        if (item.LastResult is not null && result != item.LastResult)
        {
            await PublishAsync(IAWConstants.Events.TrackingChanged, new Dictionary<string, object>
            {
                ["TrackingId"] = item.Id,
                ["Description"] = item.Description,
                ["PreviousResult"] = item.LastResult,
                ["CurrentResult"] = result
            }, ct);
        }

        durableState.TrackingItems[item.Id] = item with { LastResult = result };
    }

    [Description("Start tracking something on a schedule")]
    private async Task<string> StartTracking(
        [Description("What to track")] string description,
        [Description("Check interval in minutes")] int intervalMinutes)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var item = new TrackingItem(id, description, interval, DateTimeOffset.UtcNow, null, null);
        await StartTrackingAsync(id, item, interval, AgentCancellation);
        return $"Tracking started with ID: {id} — checking every {intervalMinutes} minutes";
    }

    [Description("Stop tracking by ID")]
    private async Task<string> StopTracking([Description("Tracking ID to stop")] string trackingId)
    {
        if (!durableState.TrackingItems.ContainsKey(trackingId)) return $"Tracking '{trackingId}' not found";
        await StopTrackingAsync(trackingId, AgentCancellation);
        return $"Tracking '{trackingId}' stopped";
    }

    [Description("List all active tracking items")]
    private Task<string> ListTracking()
    {
        if (!durableState.TrackingItems.Any()) return Task.FromResult("No active tracking items");
        var sb = new StringBuilder();
        foreach (var kvp in durableState.TrackingItems)
        {
            var item = kvp.Value;
            var lastCheck = item.LastCheckAt?.ToString("g") ?? "never";
            sb.AppendLine($"- [{item.Id}] {item.Description} (every {item.Interval.TotalMinutes}min, last: {lastCheck})");
        }
        return Task.FromResult(sb.ToString());
    }
}
