using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Core;

public class Agent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : DurableGrain, IAgent, IRemindable
{
    private const string TrackingReminderName = "agent-tracking";
    private const string TrackingKey = "status";
    private static readonly TimeSpan MinimumReminderPeriod = TimeSpan.FromMinutes(1);

    private static readonly string[] DefaultCapabilities =
    [
        "state",
        "history",
        "events",
        "notifications",
        "tracking",
        "streams",
        "tools"
    ];

    private IGrainTimer? _trackingTimer;

    public string Id => this.GetPrimaryKeyString();
    public virtual string DisplayName => Id;
    public virtual string SystemPrompt => string.Empty;
    public virtual IReadOnlyList<AITool> DefineTools() => [];

    protected AIAgent? Llm { get; private set; }

    public virtual void Activate(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        var tools = DefineTools();
        Llm = chatClient.AsAIAgent(SystemPrompt, Id, DisplayName, [.. tools], null, null);
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

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
                await WriteStateAsync(cancellationToken);
            }
        }
    }

    public virtual Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var metadata = new AgentMetadata
        {
            Id = this.GetPrimaryKeyString(),
            DisplayName = DisplayName,
            Capabilities = [.. DefaultCapabilities]
        };

        return Task.FromResult(metadata);
    }

    // -- State behavior --

    public async Task SetStateAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("State key cannot be empty.", nameof(key));

        ct.ThrowIfCancellationRequested();
        values[key] = value;
        await WriteStateAsync(ct);
    }

    public Task<string?> GetStateValueAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("State key cannot be empty.", nameof(key));

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(values.TryGetValue(key, out var value) ? value : null);
    }

    public Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in values)
            snapshot[kvp.Key] = kvp.Value;

        return Task.FromResult(snapshot);
    }

    public async Task<int> IncrementAsync(string counterKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(counterKey))
            throw new ArgumentException("Counter key cannot be empty.", nameof(counterKey));

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

    // -- History behavior --

    public async Task AddHistoryAsync(string role, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role cannot be empty.", nameof(role));

        ct.ThrowIfCancellationRequested();
        var entry = new AgentHistoryEntry
        {
            Role = role,
            Content = content ?? string.Empty,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        history.Add(entry);

        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-history", entry);
    }

    public Task<List<AgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = history
            .Select(entry => new AgentHistoryEntry
            {
                Role = entry.Role,
                Content = entry.Content,
                TimestampUtc = entry.TimestampUtc
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    // -- Events behavior --

    public async Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event name cannot be empty.", nameof(name));

        ct.ThrowIfCancellationRequested();
        var entry = new AgentEventRecord
        {
            Name = name,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        events.Add(entry);

        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-events", entry);
    }

    public Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = events
            .Select(entry => new AgentEventRecord
            {
                Name = entry.Name,
                Payload = entry.Payload,
                TimestampUtc = entry.TimestampUtc
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    // -- Notifications behavior --

    public async Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic cannot be empty.", nameof(topic));

        if (string.IsNullOrWhiteSpace(subscriberAgentId))
            throw new ArgumentException("Subscriber id cannot be empty.", nameof(subscriberAgentId));

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
        var notification = new NotificationEnvelope
        {
            Topic = topic,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        await NotifyAsync(notification, ct);
    }

    public async Task NotifyAsync(NotificationEnvelope notification, CancellationToken ct = default)
    {
        var normalized = NormalizeNotificationEnvelope(notification);

        ct.ThrowIfCancellationRequested();
        await PublishEventAsync(normalized.Topic, normalized.Payload, ct);

        if (!subscriptions.TryGetValue(normalized.Topic, out var subscriberIds) || subscriberIds.Count == 0)
            return;

        var targets = subscriberIds.ToArray();
        foreach (var subscriberId in targets)
        {
            ct.ThrowIfCancellationRequested();
            var subscriber = GrainFactory.GetGrain<IAgent>(subscriberId);
            await subscriber.ReceiveNotificationAsync(CloneNotificationEnvelope(normalized), ct);
        }
    }

    public async Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default)
    {
        var notification = new NotificationEnvelope
        {
            Topic = topic,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        await ReceiveNotificationAsync(notification, ct);
    }

    public async Task ReceiveNotificationAsync(NotificationEnvelope notification, CancellationToken ct = default)
    {
        var normalized = NormalizeNotificationEnvelope(notification);

        ct.ThrowIfCancellationRequested();
        var entry = new NotificationRecord
        {
            Topic = normalized.Topic,
            Payload = normalized.Payload,
            TimestampUtc = normalized.TimestampUtc,
            ContentType = normalized.ContentType,
            Schema = normalized.Schema,
            SchemaVersion = normalized.SchemaVersion,
            MessageId = normalized.MessageId,
            CorrelationId = normalized.CorrelationId,
            Headers = normalized.Headers
        };

        notifications.Add(entry);

        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-notifications", entry);
    }

    public Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = notifications
            .Select(entry => new NotificationRecord
            {
                Topic = entry.Topic,
                Payload = entry.Payload,
                TimestampUtc = entry.TimestampUtc,
                ContentType = entry.ContentType,
                Schema = entry.Schema,
                SchemaVersion = entry.SchemaVersion,
                MessageId = entry.MessageId,
                CorrelationId = entry.CorrelationId,
                Headers = entry.Headers is { Count: > 0 }
                    ? new Dictionary<string, string>(entry.Headers, StringComparer.OrdinalIgnoreCase)
                    : []
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    // -- Tracking behavior --

    public async Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");

        if (maxTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTicks), "Max ticks must be greater than zero.");

        ct.ThrowIfCancellationRequested();

        tracking[TrackingKey] = new AgentTrackingStatus
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

    public Task<AgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GetTrackingStatusSnapshot());
    }

    // -- Tools behavior --

    public virtual async Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));

        ct.ThrowIfCancellationRequested();

        var tools = DefineTools();
        var function = tools.OfType<AIFunction>()
            .FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (function is null)
            throw new InvalidOperationException($"Tool '{toolName}' was not found.");

        AgentObservability.RecordToolCall();

        var rawArgs = arguments is null
            ? [with(StringComparer.OrdinalIgnoreCase)]
            : arguments.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value, StringComparer.OrdinalIgnoreCase);

        var result = await function.InvokeAsync([with(rawArgs)], ct);
        return result?.ToString();
    }

    // -- Streams behavior --

    public async Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(streamNamespace))
            throw new ArgumentException("Stream namespace cannot be empty.", nameof(streamNamespace));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Stream message cannot be empty.", nameof(message));

        ct.ThrowIfCancellationRequested();
        var streamProvider = this.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));
        await stream.OnNextAsync(message);
    }

    // -- LLM streaming (not on grain interface) --

    public virtual async IAsyncEnumerable<string> SendAsync(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = AgentObservability.ActivitySource.StartActivity("agent.send", ActivityKind.Internal);
        activity?.SetTag("agent.id", this.GetPrimaryKeyString());
        activity?.SetTag("agent.display_name", DisplayName);

        AgentObservability.RecordSend();

        var inputMessage = message ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(inputMessage))
            await AddHistoryAsync("user", inputMessage, ct);

        if (Llm is null)
            yield break;

        var assistantText = new StringBuilder();
        await using var updates = Llm.RunStreamingAsync(inputMessage, cancellationToken: ct).GetAsyncEnumerator(ct);
        while (true)
        {
            AgentResponseUpdate update;
            try
            {
                if (!await updates.MoveNextAsync())
                    break;
                update = updates.Current;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                AgentObservability.RecordFailure();
                throw;
            }

            if (update.Text is { Length: > 0 } text)
            {
                assistantText.Append(text);
                yield return text;
            }
        }

        if (assistantText.Length > 0)
            await AddHistoryAsync("assistant", assistantText.ToString(), ct);
    }

    // -- IRemindable --

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, TrackingReminderName, StringComparison.Ordinal))
            return;

        await HandleTrackingTickAsync();
    }

    // -- Private tracking infrastructure --

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
            await this.UnregisterReminder(reminder);
    }

    private AgentTrackingStatus GetTrackingStatusSnapshot()
    {
        if (tracking.TryGetValue(TrackingKey, out var status))
            return CloneTrackingStatus(status);

        return new AgentTrackingStatus
        {
            IsTracking = false,
            TickCount = 0,
            StartedAtUtc = null,
            Interval = TimeSpan.Zero,
            MaxTicks = 0
        };
    }

    private static AgentTrackingStatus CloneTrackingStatus(AgentTrackingStatus status)
        => new()
        {
            IsTracking = status.IsTracking,
            TickCount = status.TickCount,
            StartedAtUtc = status.StartedAtUtc,
            Interval = status.Interval,
            MaxTicks = status.MaxTicks
        };

    private static NotificationEnvelope NormalizeNotificationEnvelope(NotificationEnvelope notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (string.IsNullOrWhiteSpace(notification.Topic))
            throw new ArgumentException("Topic cannot be empty.", nameof(notification));

        return new NotificationEnvelope
        {
            Topic = notification.Topic,
            Payload = notification.Payload ?? string.Empty,
            ContentType = string.IsNullOrWhiteSpace(notification.ContentType)
                ? "application/json"
                : notification.ContentType,
            Schema = string.IsNullOrWhiteSpace(notification.Schema) ? null : notification.Schema,
            SchemaVersion = string.IsNullOrWhiteSpace(notification.SchemaVersion) ? null : notification.SchemaVersion,
            MessageId = string.IsNullOrWhiteSpace(notification.MessageId)
                ? Guid.NewGuid().ToString("N")
                : notification.MessageId,
            CorrelationId = string.IsNullOrWhiteSpace(notification.CorrelationId) ? null : notification.CorrelationId,
            Headers = notification.Headers is { Count: > 0 }
                ? new Dictionary<string, string>(notification.Headers, StringComparer.OrdinalIgnoreCase)
                : [],
            TimestampUtc = notification.TimestampUtc == default
                ? DateTimeOffset.UtcNow
                : notification.TimestampUtc
        };
    }

    private static NotificationEnvelope CloneNotificationEnvelope(NotificationEnvelope notification)
        => new()
        {
            Topic = notification.Topic,
            Payload = notification.Payload,
            ContentType = notification.ContentType,
            Schema = notification.Schema,
            SchemaVersion = notification.SchemaVersion,
            MessageId = notification.MessageId,
            CorrelationId = notification.CorrelationId,
            Headers = notification.Headers is { Count: > 0 }
                ? new Dictionary<string, string>(notification.Headers, StringComparer.OrdinalIgnoreCase)
                : [],
            TimestampUtc = notification.TimestampUtc
        };

    private Task PublishBehaviorStreamAsync<TPayload>(string streamNamespace, TPayload payload)
    {
        var streamProvider = this.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<TPayload>(StreamId.Create(streamNamespace, this.GetPrimaryKeyString()));
        return stream.OnNextAsync(payload);
    }
}
