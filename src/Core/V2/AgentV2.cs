using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Diagnostics;
using System.Text.Json;

namespace Core.V2;

public abstract class AgentV2(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking)
    : DurableGrain, IAgentV2, IRemindable
{
    private const string ScheduleReminderName = "v2-schedule";
    private const string ScheduleKey = "__v2-schedule-status";
    private static readonly TimeSpan MinimumReminderPeriod = TimeSpan.FromMinutes(1);

    private IGrainTimer? _scheduleTimer;

    protected IDurableList<AgentMessage> Messages => messages;
    protected IDurableDictionary<string, string> Memory => memory;
    protected IDurableList<AgentEvent> Events => events;

    protected string AgentId => this.GetPrimaryKeyString();

    protected abstract AgentProfile Profile { get; }

    protected virtual IAgentV2 ResolveSubscriber(string subscriberId)
        => GrainFactory.GetGrain<IAgentV2>(subscriberId);

    protected virtual Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default)
        => Task.FromResult(new AgentReply { Output = "Not implemented" });

    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    protected virtual Task OnScheduleTickAsync(int tickCount, CancellationToken ct = default)
        => Task.CompletedTask;

    // -- Lifecycle --

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        var status = GetScheduleStatusSnapshot();
        if (status.IsRunning &&
            status.Interval > TimeSpan.Zero &&
            (!status.MaxTicks.HasValue || status.TickCount < status.MaxTicks.Value))
        {
            await StartScheduleTimerAsync(status.Interval);
        }
        else
        {
            await StopScheduleTimerAsync();

            if (status.IsRunning)
            {
                status.IsRunning = false;
                tracking[ScheduleKey] = SerializeScheduleStatus(status);
                await WriteStateAsync(cancellationToken);
            }
        }
    }

    // -- Profile --

    public Task<AgentProfile> GetProfileAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Profile);
    }

    // -- Respond --

    public async Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct = default)
    {
        using var activity = AgentObservability.ActivitySource.StartActivity("agent.respond", ActivityKind.Internal);
        activity?.SetTag("agent.id", AgentId);
        activity?.SetTag("agent.display_name", Profile.DisplayName);

        AgentObservability.RecordSend();

        ct.ThrowIfCancellationRequested();

        var userMessage = new AgentMessage
        {
            Role = "user",
            Content = request.Input ?? string.Empty,
            TimestampUtc = request.TimestampUtc
        };
        messages.Add(userMessage);
        await WriteStateAsync(ct);

        AgentReply reply;
        try
        {
            reply = await OnRespondAsync(request, ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            AgentObservability.RecordFailure();
            throw;
        }

        var assistantMessage = new AgentMessage
        {
            Role = "assistant",
            Content = reply.Output ?? string.Empty,
            TimestampUtc = reply.TimestampUtc
        };
        messages.Add(assistantMessage);
        await WriteStateAsync(ct);

        await PublishBehaviorStreamAsync("agent-history", assistantMessage);

        return reply;
    }

    // -- Messages --

    public async Task AppendMessageAsync(AgentMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ct.ThrowIfCancellationRequested();

        messages.Add(new AgentMessage
        {
            MessageId = message.MessageId,
            Role = message.Role,
            Content = message.Content,
            TimestampUtc = message.TimestampUtc == default ? DateTimeOffset.UtcNow : message.TimestampUtc,
            Metadata = message.Metadata is { Count: > 0 }
                ? new Dictionary<string, string>(message.Metadata, StringComparer.OrdinalIgnoreCase)
                : []
        });

        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-history", message);
    }

    public Task<List<AgentMessage>> QueryMessagesAsync(AgentMessageQuery? query = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IEnumerable<AgentMessage> result = messages.Select(CloneMessage);

        if (query is not null)
        {
            if (!string.IsNullOrWhiteSpace(query.Role))
                result = result.Where(m => string.Equals(m.Role, query.Role, StringComparison.OrdinalIgnoreCase));

            if (query.SinceUtc.HasValue)
                result = result.Where(m => m.TimestampUtc >= query.SinceUtc.Value);

            if (query.Descending)
                result = result.Reverse();

            if (query.Limit.HasValue && query.Limit.Value > 0)
                result = result.Take(query.Limit.Value);
        }

        return Task.FromResult(result.ToList());
    }

    // -- Memory --

    public async Task SetMemoryAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Memory key cannot be empty.", nameof(key));

        ct.ThrowIfCancellationRequested();
        memory[key] = value;
        await WriteStateAsync(ct);
    }

    public Task<string?> GetMemoryAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Memory key cannot be empty.", nameof(key));

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(memory.TryGetValue(key, out var v) ? v : null);
    }

    // -- Events --

    public async Task AppendEventAsync(AgentEvent agentEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);

        if (string.IsNullOrWhiteSpace(agentEvent.Type))
            throw new ArgumentException("Event type cannot be empty.", nameof(agentEvent));

        ct.ThrowIfCancellationRequested();

        var entry = new AgentEvent
        {
            EventId = agentEvent.EventId,
            Type = agentEvent.Type,
            Payload = agentEvent.Payload,
            TimestampUtc = agentEvent.TimestampUtc == default ? DateTimeOffset.UtcNow : agentEvent.TimestampUtc,
            Metadata = agentEvent.Metadata is { Count: > 0 }
                ? new Dictionary<string, string>(agentEvent.Metadata, StringComparer.OrdinalIgnoreCase)
                : []
        };

        events.Add(entry);
        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-events", entry);
    }

    public Task<List<AgentEvent>> QueryEventsAsync(AgentEventQuery? query = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IEnumerable<AgentEvent> result = events.Select(CloneEvent);

        if (query is not null)
        {
            if (!string.IsNullOrWhiteSpace(query.Type))
                result = result.Where(e => string.Equals(e.Type, query.Type, StringComparison.OrdinalIgnoreCase));

            if (query.SinceUtc.HasValue)
                result = result.Where(e => e.TimestampUtc >= query.SinceUtc.Value);

            if (query.Descending)
                result = result.Reverse();

            if (query.Limit.HasValue && query.Limit.Value > 0)
                result = result.Take(query.Limit.Value);
        }

        return Task.FromResult(result.ToList());
    }

    // -- Notifications --

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

    public async Task NotifyAsync(NotificationEnvelope envelope, CancellationToken ct = default)
    {
        var normalized = NormalizeNotificationEnvelope(envelope);

        ct.ThrowIfCancellationRequested();

        await AppendEventAsync(new AgentEvent
        {
            Type = normalized.Topic,
            Payload = normalized.Payload
        }, ct);

        if (!subscriptions.TryGetValue(normalized.Topic, out var subscriberIds) || subscriberIds.Count == 0)
            return;

        var targets = subscriberIds.ToArray();
        foreach (var subscriberId in targets)
        {
            ct.ThrowIfCancellationRequested();
            var subscriber = ResolveSubscriber(subscriberId);
            await subscriber.ReceiveNotificationAsync(CloneNotificationEnvelope(normalized), ct);
        }
    }

    public async Task ReceiveNotificationAsync(NotificationEnvelope envelope, CancellationToken ct = default)
    {
        var normalized = NormalizeNotificationEnvelope(envelope);

        ct.ThrowIfCancellationRequested();

        var record = new NotificationRecord
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

        notifications.Add(record);
        await WriteStateAsync(ct);
        await PublishBehaviorStreamAsync("agent-notifications", record);
    }

    public Task<List<NotificationRecord>> QueryNotificationsAsync(CancellationToken ct = default)
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

    // -- Scheduling --

    public async Task StartScheduleAsync(TimeSpan interval, int? maxTicks = null, CancellationToken ct = default)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero.");

        ct.ThrowIfCancellationRequested();

        var status = new ScheduleStatus
        {
            IsRunning = true,
            TickCount = 0,
            Interval = interval,
            MaxTicks = maxTicks
        };

        tracking[ScheduleKey] = SerializeScheduleStatus(status);
        await StartScheduleTimerAsync(interval);
        await WriteStateAsync(ct);
    }

    public async Task StopScheduleAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var status = GetScheduleStatusSnapshot();
        status.IsRunning = false;
        tracking[ScheduleKey] = SerializeScheduleStatus(status);

        await StopScheduleTimerAsync();
        await WriteStateAsync(ct);
    }

    public Task<ScheduleStatus> GetScheduleStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GetScheduleStatusSnapshot());
    }

    // -- Streams --

    public async Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(streamNamespace))
            throw new ArgumentException("Stream namespace cannot be empty.", nameof(streamNamespace));

        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Stream message cannot be empty.", nameof(message));

        ct.ThrowIfCancellationRequested();
        var provider = this.GetStreamProvider("agents");
        var stream = provider.GetStream<string>(StreamId.Create(streamNamespace, streamId));
        await stream.OnNextAsync(message);
    }

    // -- Tools --

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
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : arguments.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value, StringComparer.OrdinalIgnoreCase);

        var result = await function.InvokeAsync(new AIFunctionArguments(rawArgs), ct);
        return result?.ToString();
    }

    // -- LLM helper for derived agents --

    protected async Task<AgentReply> RespondWithLlmAsync(IChatClient chatClient, AgentRequest request, CancellationToken ct = default)
    {
        using var activity = AgentObservability.ActivitySource.StartActivity("agent.llm", ActivityKind.Internal);
        activity?.SetTag("agent.id", AgentId);

        var chatMessages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(Profile.Instructions))
            chatMessages.Add(new ChatMessage(ChatRole.System, Profile.Instructions));

        // _messages already contains the current user message (added by RespondAsync)
        foreach (var msg in messages)
            chatMessages.Add(new ChatMessage(new ChatRole(msg.Role), msg.Content));

        var options = new ChatOptions { Tools = [.. DefineTools()] };
        var response = await chatClient.GetResponseAsync(chatMessages, options, ct);
        var output = string.Join("", response.Messages.Where(m => m.Role == ChatRole.Assistant).Select(m => m.Text));
        return new AgentReply { Output = output, ModelId = response.ModelId };
    }

    // -- IRemindable --

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, ScheduleReminderName, StringComparison.Ordinal))
            return;

        await HandleScheduleTickAsync();
    }

    // -- Private scheduling infrastructure --

    private async Task ScheduleTimerTickAsync() => await HandleScheduleTickAsync();

    private async Task HandleScheduleTickAsync()
    {
        var status = GetScheduleStatusSnapshot();
        if (!status.IsRunning)
        {
            await StopScheduleTimerAsync();
            return;
        }

        status.TickCount++;

        // Persist FIRST so crash during callback doesn't lose the count
        tracking[ScheduleKey] = SerializeScheduleStatus(status);
        await WriteStateAsync();

        await OnScheduleTickAsync(status.TickCount);

        if (status.MaxTicks.HasValue && status.TickCount >= status.MaxTicks.Value)
        {
            status.IsRunning = false;
            tracking[ScheduleKey] = SerializeScheduleStatus(status);
            await StopScheduleTimerAsync();
            await WriteStateAsync();
        }
    }

    private async Task StartScheduleTimerAsync(TimeSpan interval)
    {
        await StopScheduleTimerAsync();

        if (interval >= MinimumReminderPeriod)
        {
            await this.RegisterOrUpdateReminder(
                ScheduleReminderName,
                interval,
                interval);
        }
        else
        {
            _scheduleTimer = this.RegisterGrainTimer(
                ScheduleTimerTickAsync,
                interval,
                interval);
        }
    }

    private async Task StopScheduleTimerAsync()
    {
        _scheduleTimer?.Dispose();
        _scheduleTimer = null;

        var reminder = await this.GetReminder(ScheduleReminderName);
        if (reminder is not null)
            await this.UnregisterReminder(reminder);
    }

    private ScheduleStatus GetScheduleStatusSnapshot()
    {
        if (tracking.TryGetValue(ScheduleKey, out var json))
            return DeserializeScheduleStatus(json);

        return new ScheduleStatus
        {
            IsRunning = false,
            TickCount = 0,
            Interval = TimeSpan.Zero,
            MaxTicks = null
        };
    }

    private static string SerializeScheduleStatus(ScheduleStatus status)
        => JsonSerializer.Serialize(status);

    private static ScheduleStatus DeserializeScheduleStatus(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScheduleStatus>(json) ?? new ScheduleStatus();
        }
        catch
        {
            return new ScheduleStatus();
        }
    }

    // -- Private helpers --

    private static AgentMessage CloneMessage(AgentMessage m) => new()
    {
        MessageId = m.MessageId,
        Role = m.Role,
        Content = m.Content,
        TimestampUtc = m.TimestampUtc,
        Metadata = m.Metadata is { Count: > 0 }
            ? new Dictionary<string, string>(m.Metadata, StringComparer.OrdinalIgnoreCase)
            : []
    };

    private static AgentEvent CloneEvent(AgentEvent e) => new()
    {
        EventId = e.EventId,
        Type = e.Type,
        Payload = e.Payload,
        TimestampUtc = e.TimestampUtc,
        Metadata = e.Metadata is { Count: > 0 }
            ? new Dictionary<string, string>(e.Metadata, StringComparer.OrdinalIgnoreCase)
            : []
    };

    private static NotificationEnvelope NormalizeNotificationEnvelope(NotificationEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrWhiteSpace(envelope.Topic))
            throw new ArgumentException("Topic cannot be empty.", nameof(envelope));

        return new NotificationEnvelope
        {
            Topic = envelope.Topic,
            Payload = envelope.Payload ?? string.Empty,
            ContentType = string.IsNullOrWhiteSpace(envelope.ContentType)
                ? "application/json"
                : envelope.ContentType,
            Schema = string.IsNullOrWhiteSpace(envelope.Schema) ? null : envelope.Schema,
            SchemaVersion = string.IsNullOrWhiteSpace(envelope.SchemaVersion) ? null : envelope.SchemaVersion,
            MessageId = string.IsNullOrWhiteSpace(envelope.MessageId)
                ? Guid.NewGuid().ToString("N")
                : envelope.MessageId,
            CorrelationId = string.IsNullOrWhiteSpace(envelope.CorrelationId) ? null : envelope.CorrelationId,
            Headers = envelope.Headers is { Count: > 0 }
                ? new Dictionary<string, string>(envelope.Headers, StringComparer.OrdinalIgnoreCase)
                : [],
            TimestampUtc = envelope.TimestampUtc == default
                ? DateTimeOffset.UtcNow
                : envelope.TimestampUtc
        };
    }

    private static NotificationEnvelope CloneNotificationEnvelope(NotificationEnvelope envelope) => new()
    {
        Topic = envelope.Topic,
        Payload = envelope.Payload,
        ContentType = envelope.ContentType,
        Schema = envelope.Schema,
        SchemaVersion = envelope.SchemaVersion,
        MessageId = envelope.MessageId,
        CorrelationId = envelope.CorrelationId,
        Headers = envelope.Headers is { Count: > 0 }
            ? new Dictionary<string, string>(envelope.Headers, StringComparer.OrdinalIgnoreCase)
            : [],
        TimestampUtc = envelope.TimestampUtc
    };

    private Task PublishBehaviorStreamAsync<TPayload>(string streamNamespace, TPayload payload)
    {
        var provider = this.GetStreamProvider("agents");
        var stream = provider.GetStream<TPayload>(StreamId.Create(streamNamespace, this.GetPrimaryKeyString()));
        return stream.OnNextAsync(payload);
    }
}
