using Grpc.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Core.Memory;

public sealed class IawMemoryProvider(
    QdrantClient qdrantClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<IawMemoryProvider> logger)
    : MessageAIContextProvider, IMemoryLookup
{
    const int TopK = 5;
    const string SourceMessageIdKey = "iaw.sourceTelegramMsgId";

    protected override async ValueTask<IEnumerable<AIChatMessage>> ProvideMessagesAsync(
        MessageAIContextProvider.InvokingContext context, CancellationToken cancellationToken = default)
    {
        var userId = ReadUserId();
        if (userId is null)
            return Array.Empty<AIChatMessage>();

        var query = BuildQueryText(context.RequestMessages);
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<AIChatMessage>();

        var collection = CollectionFor(userId);
        try
        {
            if (!await qdrantClient.CollectionExistsAsync(collection, cancellationToken))
                return Array.Empty<AIChatMessage>();

            var vector = (await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken))[0].Vector.ToArray();
            var results = await qdrantClient.SearchAsync(collection, vector, limit: TopK, cancellationToken: cancellationToken);
            if (results.Count == 0)
                return Array.Empty<AIChatMessage>();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Memories");
            foreach (var point in results)
            {
                var date = point.Payload.TryGetValue("createdAtTicks", out var ticks) && ticks.KindCase == Value.KindOneofCase.IntegerValue
                    ? new DateTime(ticks.IntegerValue, DateTimeKind.Utc).ToString("yyyy-MM-dd")
                    : "";
                var role = point.Payload.TryGetValue("role", out var r) ? r.StringValue : "user";
                var content = point.Payload.TryGetValue("content", out var c) ? c.StringValue : "";
                if (string.IsNullOrEmpty(content)) continue;
                sb.AppendLine($"[{date}] {role}: {content}");
            }

            return new[] { new AIChatMessage(ChatRole.System, sb.ToString()) };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IawMemoryProvider recall failed for user {UserId}", userId);
            return Array.Empty<AIChatMessage>();
        }
    }

    protected override async ValueTask StoreAIContextAsync(
        AIContextProvider.InvokedContext context, CancellationToken cancellationToken = default)
    {
        var userId = ReadUserId();
        if (userId is null) return;

        var threadId = ReadThreadId();

        var toStore = new List<AIChatMessage>();
        if (context.RequestMessages is { } requestMessages)
            foreach (var msg in requestMessages)
                if (msg.Role == ChatRole.User && !string.IsNullOrWhiteSpace(msg.Text))
                    toStore.Add(msg);
        if (context.ResponseMessages is { } responseMessages)
            foreach (var msg in responseMessages)
                if (msg.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(msg.Text))
                    toStore.Add(msg);

        if (toStore.Count == 0) return;

        var collection = CollectionFor(userId);
        try
        {
            var texts = toStore.Select(m => m.Text!).ToList();
            var embeddings = await embeddingGenerator.GenerateAsync(texts, cancellationToken: cancellationToken);
            if (embeddings.Count == 0) return;

            var dims = (uint)embeddings[0].Vector.Length;

            if (!await qdrantClient.CollectionExistsAsync(collection, cancellationToken))
            {
                try
                {
                    await qdrantClient.CreateCollectionAsync(
                        collection,
                        new VectorParams { Size = dims, Distance = Distance.Cosine },
                        cancellationToken: cancellationToken);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists) { }
            }

            var points = new List<PointStruct>(toStore.Count);
            for (var i = 0; i < toStore.Count; i++)
            {
                var msg = toStore[i];
                var point = new PointStruct
                {
                    Id = (PointId)Guid.NewGuid(),
                    Vectors = embeddings[i].Vector.ToArray(),
                    Payload =
                    {
                        ["content"] = msg.Text!,
                        ["userId"] = userId,
                        ["role"] = msg.Role.Value,
                        ["createdAtTicks"] = DateTimeOffset.UtcNow.UtcTicks
                    }
                };
                if (threadId is not null)
                    point.Payload["threadId"] = threadId;
                if (msg.AdditionalProperties is { } props
                    && props.TryGetValue(SourceMessageIdKey, out var srcId)
                    && srcId is not null)
                    point.Payload["sourceTelegramMsgId"] = srcId.ToString() ?? "";

                points.Add(point);
            }

            await qdrantClient.UpsertAsync(collection, points, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IawMemoryProvider store failed for user {UserId}", userId);
        }
    }

    public async Task<MemoryHit?> LookupOriginAsync(string userId, string question, CancellationToken ct)
    {
        var collection = CollectionFor(userId);
        try
        {
            if (!await qdrantClient.CollectionExistsAsync(collection, ct))
                return null;

            var vector = (await embeddingGenerator.GenerateAsync([question], cancellationToken: ct))[0].Vector.ToArray();
            var results = await qdrantClient.SearchAsync(collection, vector, limit: 1, cancellationToken: ct);
            if (results.Count == 0) return null;

            var p = results[0].Payload;
            var content = p.TryGetValue("content", out var c) ? c.StringValue : "";
            var role = p.TryGetValue("role", out var r) ? r.StringValue : "user";
            var createdAt = p.TryGetValue("createdAtTicks", out var t) && t.KindCase == Value.KindOneofCase.IntegerValue
                ? new DateTimeOffset(new DateTime(t.IntegerValue, DateTimeKind.Utc))
                : DateTimeOffset.MinValue;
            var threadId = p.TryGetValue("threadId", out var th) ? th.StringValue : null;
            var srcMsgId = p.TryGetValue("sourceTelegramMsgId", out var s) ? s.StringValue : null;

            return new MemoryHit(content, role, createdAt, threadId, string.IsNullOrEmpty(srcMsgId) ? null : srcMsgId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IawMemoryProvider lookup failed for user {UserId}", userId);
            return null;
        }
    }

    static string CollectionFor(string userId) => $"user-memory-{userId}";

    static string BuildQueryText(IEnumerable<AIChatMessage> messages)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User);
        return lastUser?.Text ?? string.Empty;
    }

    static string? ReadUserId()
    {
        var bag = AIAgent.CurrentRunContext?.Session?.StateBag;
        if (bag is null) return null;
        return bag.TryGetValue<string>("iaw.userId", out var userId) ? userId : null;
    }

    static string? ReadThreadId()
    {
        var bag = AIAgent.CurrentRunContext?.Session?.StateBag;
        if (bag is null) return null;
        return bag.TryGetValue<string>("iaw.threadId", out var threadId) ? threadId : null;
    }
}
