using Core.V2;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Core;

public class Agent(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking)
    : AgentV2(messages, memory, events, subscriptions, notifications, tracking), IAgent
{
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

    public string Id => this.GetPrimaryKeyString();
    public virtual string DisplayName => Id;
    public virtual string SystemPrompt => string.Empty;

    protected AIAgent? Llm { get; private set; }

    protected override IAgentV2 ResolveSubscriber(string subscriberId)
        => GrainFactory.GetGrain<IAgent>(subscriberId);

    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = DisplayName,
        Instructions = SystemPrompt,
        Capabilities = [.. DefaultCapabilities]
    };

    public virtual void Activate(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        var tools = DefineTools();
        Llm = chatClient.AsAIAgent(SystemPrompt, Id, DisplayName, [.. tools], null, null);
    }

    // -- Metadata (V1 only) --

    public virtual Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new AgentMetadata
        {
            Id = this.GetPrimaryKeyString(),
            DisplayName = DisplayName,
            Capabilities = [.. DefaultCapabilities]
        });
    }

    // -- State (V1 → V2 Memory) --

    public async Task SetStateAsync(string key, string value, CancellationToken ct = default)
    {
        await SetMemoryAsync(key, value, ct);
    }

    public async Task<string?> GetStateValueAsync(string key, CancellationToken ct = default)
    {
        return await GetMemoryAsync(key, ct);
    }

    public Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in Memory)
            snapshot[kvp.Key] = kvp.Value;

        return Task.FromResult(snapshot);
    }

    public async Task<int> IncrementAsync(string counterKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(counterKey))
            throw new ArgumentException("Counter key cannot be empty.", nameof(counterKey));

        ct.ThrowIfCancellationRequested();
        var current = Memory.TryGetValue(counterKey, out var raw) &&
                      int.TryParse(raw, out var parsed)
            ? parsed
            : 0;

        var next = current + 1;
        Memory[counterKey] = next.ToString(CultureInfo.InvariantCulture);
        await WriteStateAsync(ct);
        return next;
    }

    // -- History (V1 shim over V2 Messages storage, publishes V1 stream type) --

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

        Messages.Add(new AgentMessage
        {
            Role = entry.Role,
            Content = entry.Content,
            TimestampUtc = entry.TimestampUtc
        });

        await WriteStateAsync(ct);
        await PublishV1StreamAsync("agent-history", entry);
    }

    public Task<List<AgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = Messages
            .Select(m => new AgentHistoryEntry
            {
                Role = m.Role,
                Content = m.Content,
                TimestampUtc = m.TimestampUtc
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    // -- Events (V1 shim over V2 Events storage, publishes V1 stream type) --

    public async Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Event name cannot be empty.", nameof(name));

        ct.ThrowIfCancellationRequested();
        var record = new AgentEventRecord
        {
            Name = name,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        Events.Add(new AgentEvent
        {
            Type = record.Name,
            Payload = record.Payload,
            TimestampUtc = record.TimestampUtc
        });

        await WriteStateAsync(ct);
        await PublishV1StreamAsync("agent-events", record);
    }

    public Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = Events
            .Select(e => new AgentEventRecord
            {
                Name = e.Type,
                Payload = e.Payload,
                TimestampUtc = e.TimestampUtc
            })
            .ToList();

        return Task.FromResult(snapshot);
    }

    // -- Notifications (V1 convenience overloads → V2) --

    public async Task NotifyAsync(string topic, string payload, CancellationToken ct = default)
    {
        await NotifyAsync(new NotificationEnvelope
        {
            Topic = topic,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        }, ct);
    }

    public async Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default)
    {
        await ReceiveNotificationAsync(new NotificationEnvelope
        {
            Topic = topic,
            Payload = payload,
            TimestampUtc = DateTimeOffset.UtcNow
        }, ct);
    }

    public async Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default)
    {
        return await QueryNotificationsAsync(ct);
    }

    // -- Tracking (V1 → V2 Scheduling) --

    public async Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default)
    {
        if (maxTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTicks), "Max ticks must be greater than zero.");

        await StartScheduleAsync(interval, maxTicks, ct);
    }

    public async Task StopTrackingAsync(CancellationToken ct = default)
    {
        await StopScheduleAsync(ct);
    }

    public async Task<AgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default)
    {
        var v2Status = await GetScheduleStatusAsync(ct);
        return new AgentTrackingStatus
        {
            IsTracking = v2Status.IsRunning,
            TickCount = v2Status.TickCount,
            Interval = v2Status.Interval,
            MaxTicks = v2Status.MaxTicks ?? 0
        };
    }

    // -- V1 stream helper --

    private Task PublishV1StreamAsync<TPayload>(string streamNamespace, TPayload payload)
    {
        var provider = this.GetStreamProvider("agents");
        var stream = provider.GetStream<TPayload>(StreamId.Create(streamNamespace, this.GetPrimaryKeyString()));
        return stream.OnNextAsync(payload);
    }

    // -- LLM streaming (V1 only, not on grain interface) --

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
}
