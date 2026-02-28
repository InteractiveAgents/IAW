using System.Globalization;
using Orleans;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;

namespace Core;

public class OrleansAgentGrain(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<OrleansAgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<OrleansAgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<OrleansAgentNotificationRecord> notifications,
    [Memory("agent-config")] IDurableDictionary<string, OrleansAgentConfig> configurations,
    [Memory("agent-tracking")] IDurableDictionary<string, OrleansAgentTrackingStatus> tracking)
    : DurableGrain, IOrleansAgentGrain, IRemindable
{
    private const string TrackingReminderName = "agent-tracking";
    private const string TrackingKey = "status";
    private const string ConfigKey = "default";
    private static readonly TimeSpan MinimumReminderPeriod = TimeSpan.FromMinutes(1);

    private static readonly string[] DefaultCapabilities =
    [
        "state",
        "history",
        "events",
        "notifications",
        "tracking",
        "streams",
        "dynamic-config",
        "tools"
    ];

    private IGrainTimer? _trackingTimer;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        var mutated = false;
        if (!configurations.TryGetValue(ConfigKey, out _))
        {
            configurations[ConfigKey] = OrleansAgentConfig.CreateDefault();
            mutated = true;
        }

        var trackingStatus = GetTrackingStatusSnapshot();
        if (trackingStatus.IsTracking &&
            trackingStatus.Interval > TimeSpan.Zero &&
            trackingStatus.MaxTicks > 0 &&
            trackingStatus.TickCount < trackingStatus.MaxTicks)
        {
            await StartTrackingScheduleAsync(trackingStatus.Interval);
        }
        else
        {
            await StopTrackingScheduleAsync();

            if (trackingStatus.IsTracking)
            {
                trackingStatus.IsTracking = false;
                tracking[TrackingKey] = CloneTrackingStatus(trackingStatus);
                mutated = true;
            }
        }

        if (mutated)
        {
            await WriteStateAsync(cancellationToken);
        }
    }

    public Task<OrleansAgentMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var metadata = new OrleansAgentMetadata
        {
            AgentId = this.GetPrimaryKeyString(),
            DisplayName = "Orleans Agent Grain",
            Capabilities = [.. DefaultCapabilities]
        };

        return Task.FromResult(metadata);
    }

    public async Task SetStateAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("State key cannot be empty.", nameof(key));
        }

        ct.ThrowIfCancellationRequested();
        values[key] = value;
        await WriteStateAsync(ct);
    }

    public Task<string?> GetStateValueAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("State key cannot be empty.", nameof(key));
        }

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(values.TryGetValue(key, out var value) ? value : null);
    }

    public Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in values)
        {
            snapshot[kvp.Key] = kvp.Value;
        }

        return Task.FromResult(snapshot);
    }

    public async Task<int> IncrementAsync(string counterKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(counterKey))
        {
            throw new ArgumentException("Counter key cannot be empty.", nameof(counterKey));
        }

        ct.ThrowIfCancellationRequested();
        var current = values.TryGetValue(counterKey, out var raw) &&
                      int.TryParse(raw, out var parsed)
            ? parsed
            : 0;

        var next = current + 1;
        values[counterKey] = next.ToString(CultureInfo.InvariantCulture);
        await WriteStateAsync(ct);
        return next;
    }

    public async Task AddHistoryAsync(string role, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Role cannot be empty.", nameof(role));
        }

        ct.ThrowIfCancellationRequested();
        var entry = new OrleansAgentHistoryEntry
        {
            Role = role,
            Content = content ?? string.Empty,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        history.Add(entry);

        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-history", entry);
    }

    public Task<List<OrleansAgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = history
            .Select(entry => new OrleansAgentHistoryEntry
            {
                Role = entry.Role,
                Content = entry.Content,
                TimestampUtc = entry.TimestampUtc
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    public async Task<IReadOnlyList<string>> SendDeterministicAsync(string message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await AddHistoryAsync("user", message, ct);

        var config = GetConfigSnapshot();
        if (!config.ResponsesEnabled)
        {
            return [];
        }

        var baseResponse = $"echo:{message}";
        if (!string.IsNullOrWhiteSpace(config.PromptPrefix))
        {
            baseResponse = $"{config.PromptPrefix}{baseResponse}";
        }

        var chunks = baseResponse
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (config.MaxResponseChunks is { } maxChunks)
        {
            chunks = chunks.Take(maxChunks).ToList();
        }

        await AddHistoryAsync("assistant", string.Join(' ', chunks), ct);
        return chunks;
    }

    public async Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Event name cannot be empty.", nameof(name));
        }

        ct.ThrowIfCancellationRequested();
        var entry = new OrleansAgentEventRecord
        {
            Name = name,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        events.Add(entry);

        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-events", entry);
    }

    public Task<List<OrleansAgentEventRecord>> GetEventsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = events
            .Select(entry => new OrleansAgentEventRecord
            {
                Name = entry.Name,
                Payload = entry.Payload,
                TimestampUtc = entry.TimestampUtc
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    public async Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic cannot be empty.", nameof(topic));
        }

        if (string.IsNullOrWhiteSpace(subscriberAgentId))
        {
            throw new ArgumentException("Subscriber id cannot be empty.", nameof(subscriberAgentId));
        }

        ct.ThrowIfCancellationRequested();

        var subscribers = subscriptions.TryGetValue(topic, out var current)
            ? current.ToList()
            : [];

        if (!subscribers.Contains(subscriberAgentId, StringComparer.Ordinal))
        {
            subscribers.Add(subscriberAgentId);
            subscriptions[topic] = subscribers;
            await WriteStateAsync(ct);
        }
    }

    public async Task NotifyAsync(string topic, string payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic cannot be empty.", nameof(topic));
        }

        ct.ThrowIfCancellationRequested();
        await PublishEventAsync(topic, payload, ct);

        if (!subscriptions.TryGetValue(topic, out var subscriberIds) || subscriberIds.Count == 0)
        {
            return;
        }

        var targets = subscriberIds.ToArray();
        foreach (var subscriberId in targets)
        {
            ct.ThrowIfCancellationRequested();
            var subscriber = GrainFactory.GetGrain<IAgent>(subscriberId);
            await subscriber.ReceiveNotificationAsync(topic, payload, ct);
        }
    }

    public async Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic cannot be empty.", nameof(topic));
        }

        ct.ThrowIfCancellationRequested();
        var entry = new OrleansAgentNotificationRecord
        {
            Topic = topic,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        notifications.Add(entry);

        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-notifications", entry);
    }

    public Task<List<OrleansAgentNotificationRecord>> GetNotificationsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = notifications
            .Select(entry => new OrleansAgentNotificationRecord
            {
                Topic = entry.Topic,
                Payload = entry.Payload,
                TimestampUtc = entry.TimestampUtc
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    public async Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");
        }

        if (maxTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTicks), "Max ticks must be greater than zero.");
        }

        ct.ThrowIfCancellationRequested();

        tracking[TrackingKey] = new OrleansAgentTrackingStatus
        {
            IsTracking = true,
            TickCount = 0,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Interval = interval,
            MaxTicks = maxTicks
        };

        await StartTrackingScheduleAsync(interval);
        await WriteStateAsync(ct);
    }

    public async Task StopTrackingAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var status = GetTrackingStatusSnapshot();
        status.IsTracking = false;
        tracking[TrackingKey] = CloneTrackingStatus(status);

        await StopTrackingScheduleAsync();
        await WriteStateAsync(ct);
    }

    public Task<OrleansAgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GetTrackingStatusSnapshot());
    }

    public async Task<OrleansAgentConfig> ConfigureAsync(OrleansAgentConfigPatch patch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ct.ThrowIfCancellationRequested();

        if (patch.MaxResponseChunks is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(patch.MaxResponseChunks), "MaxResponseChunks must be greater than zero when provided.");
        }

        var config = GetConfigSnapshot();
        config.ResponsesEnabled = patch.ResponsesEnabled ?? config.ResponsesEnabled;
        config.ToolsEnabled = patch.ToolsEnabled ?? config.ToolsEnabled;
        config.MaxResponseChunks = patch.MaxResponseChunks ?? config.MaxResponseChunks;
        config.PromptPrefix = patch.PromptPrefix ?? config.PromptPrefix;
        configurations[ConfigKey] = CloneConfig(config);
        await WriteStateAsync(ct);
        return CloneConfig(config);
    }

    public Task<OrleansAgentConfig> GetConfigurationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CloneConfig(GetConfigSnapshot()));
    }

    public async Task<int> InvokeAddNumbersToolAsync(int a, int b, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var config = GetConfigSnapshot();
        if (!config.ToolsEnabled)
        {
            throw new InvalidOperationException("Tool calls are disabled by configuration.");
        }

        var result = a + b;
        values["last-tool-result"] = result.ToString(CultureInfo.InvariantCulture);
        await WriteStateAsync(ct);
        return result;
    }

    public async Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(streamNamespace))
        {
            throw new ArgumentException("Stream namespace cannot be empty.", nameof(streamNamespace));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Stream message cannot be empty.", nameof(message));
        }

        ct.ThrowIfCancellationRequested();
        var streamProvider = this.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));
        await stream.OnNextAsync(message);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, TrackingReminderName, StringComparison.Ordinal))
        {
            return;
        }

        await HandleTrackingTickAsync();
    }

    private async Task TrackingTimerTickAsync() => await HandleTrackingTickAsync();

    private async Task HandleTrackingTickAsync()
    {
        var status = GetTrackingStatusSnapshot();
        if (!status.IsTracking)
        {
            await StopTrackingScheduleAsync();
            return;
        }

        status.TickCount++;
        if (status.TickCount >= status.MaxTicks)
        {
            status.IsTracking = false;
            await StopTrackingScheduleAsync();
        }

        tracking[TrackingKey] = CloneTrackingStatus(status);
        await WriteStateAsync();
    }

    private async Task StartTrackingScheduleAsync(TimeSpan interval)
    {
        await StopTrackingScheduleAsync();

        if (interval >= MinimumReminderPeriod)
        {
            await this.RegisterOrUpdateReminder(
                TrackingReminderName,
                interval,
                interval);
        }
        else
        {
            _trackingTimer = this.RegisterGrainTimer(
                TrackingTimerTickAsync,
                interval,
                interval);
        }
    }

    private async Task StopTrackingScheduleAsync()
    {
        _trackingTimer?.Dispose();
        _trackingTimer = null;

        var reminder = await this.GetReminder(TrackingReminderName);
        if (reminder is not null)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    private OrleansAgentTrackingStatus GetTrackingStatusSnapshot()
    {
        if (tracking.TryGetValue(TrackingKey, out var status))
        {
            return CloneTrackingStatus(status);
        }

        return new OrleansAgentTrackingStatus
        {
            IsTracking = false,
            TickCount = 0,
            StartedAtUtc = null,
            Interval = TimeSpan.Zero,
            MaxTicks = 0
        };
    }

    private OrleansAgentConfig GetConfigSnapshot() => configurations.TryGetValue(ConfigKey, out var config)
        ? CloneConfig(config)
        : OrleansAgentConfig.CreateDefault();

    private static OrleansAgentConfig CloneConfig(OrleansAgentConfig config)
        => new()
        {
            ResponsesEnabled = config.ResponsesEnabled,
            ToolsEnabled = config.ToolsEnabled,
            MaxResponseChunks = config.MaxResponseChunks,
            PromptPrefix = config.PromptPrefix
        };

    private static OrleansAgentTrackingStatus CloneTrackingStatus(OrleansAgentTrackingStatus status)
        => new()
        {
            IsTracking = status.IsTracking,
            TickCount = status.TickCount,
            StartedAtUtc = status.StartedAtUtc,
            Interval = status.Interval,
            MaxTicks = status.MaxTicks
        };

    private Task PublishBehaviorStreamAsync<TPayload>(string streamNamespace, TPayload payload)
    {
        var streamProvider = this.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<TPayload>(StreamId.Create(streamNamespace, this.GetPrimaryKeyString()));
        return stream.OnNextAsync(payload);
    }
}
