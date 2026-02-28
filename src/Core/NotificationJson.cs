using System.Text.Json;

namespace Core;

public static class NotificationJson
{
    public static NotificationEnvelope CreateEnvelope<TPayload>(
        string topic,
        TPayload payload,
        string? schema = null,
        string? schemaVersion = null,
        string? messageId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? headers = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic cannot be empty.", nameof(topic));
        }

        var serializedPayload = JsonSerializer.Serialize(payload, serializerOptions);
        return new NotificationEnvelope
        {
            Topic = topic,
            Payload = serializedPayload,
            ContentType = "application/json",
            Schema = schema,
            SchemaVersion = schemaVersion,
            MessageId = string.IsNullOrWhiteSpace(messageId)
                ? Guid.NewGuid().ToString("N")
                : messageId,
            CorrelationId = correlationId,
            Headers = headers is { Count: > 0 }
                ? new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
                : [],
            TimestampUtc = DateTimeOffset.UtcNow
        };
    }

    public static TPayload? ReadPayload<TPayload>(
        this NotificationEnvelope notification,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ValidateJsonContentType(notification.ContentType);
        if (string.IsNullOrWhiteSpace(notification.Payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TPayload>(notification.Payload, serializerOptions);
    }

    public static TPayload? ReadPayload<TPayload>(
        this NotificationRecord notification,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ValidateJsonContentType(notification.ContentType);
        if (string.IsNullOrWhiteSpace(notification.Payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TPayload>(notification.Payload, serializerOptions);
    }

    private static void ValidateJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException(
                $"Notification payload content-type '{contentType ?? "<null>"}' is not JSON.");
        }
    }
}
