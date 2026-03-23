using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Core;
using Core.Agents;
using Core.AI;
using Core.Context;
using Core.Contracts;
using Core.Ingestion;
using Core.Services;
using Core.UI;
using UIAgentResponse = Core.UI.AgentResponse;
using ChatMessage = Core.Contracts.ChatMessage;
using ContractsTextContent = Core.Contracts.TextContent;
using Core.Observability;
using Grpc.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Streams;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace IAW.Core;

[GrainType(IAWConstants.GrainTypes.Agent)]
public abstract partial class Agent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : DurableGrain, IAgent
{
    private readonly UsageCaptureChatClient _usageCapture = new(chatClient);
    private AIAgent? _agent;
    private ChatOptions? _chatOptions;
    private AgentSession? _session;
    private IReadOnlyList<ContentPart>? _currentMessageParts;
    private ChannelWriter<string>? _toolProgressWriter;

    protected void WriteToolProgress(string text)
    {
        _toolProgressWriter?.TryWrite(text);
    }

    protected virtual string Instructions => "You are a helpful AI assistant. Answer questions clearly and concisely.";
    protected virtual int MaxHistoryMessages => 100;
    protected virtual int MaxOutputTokens => 4096;
    protected IChatClient ChatClient => chatClient;
    protected IDurableList<ChatMessage> History => durableState.History;
    protected IDurableDictionary<string, StateEntry> State => durableState.State;
    protected IDurableList<AgentEvent> EventLog => durableState.EventLog;
    protected IStreamProvider StreamProvider => this.GetStreamProvider(IAWConstants.StreamProvider);
    protected virtual IReadOnlyList<IAgentContextProvider> GetContextProviders() => Array.Empty<IAgentContextProvider>();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.activate");
        activity?.SetTag("agent.type", GetType().Name);
        activity?.SetTag("agent.id", this.GetPrimaryKeyString());
        AgentTelemetry.Activations.Add(1, new TagList { { "agent.type", GetType().Name } });

        var blobStorage = ServiceProvider.GetService<BlobFileStorage>();
        _chatOptions = new ChatOptions
        {
            Instructions = Instructions,
            Tools = GetAllTools().ToList(),
            MaxOutputTokens = MaxOutputTokens
        };
        _agent = _usageCapture.AsAIAgent(new ChatClientAgentOptions
        {
            Name = this.GetPrimaryKeyString(),
            ChatOptions = _chatOptions,
            ChatHistoryProvider = new DurableChatHistoryProvider(durableState.History, MaxHistoryMessages, blobStorage, new ChatReducer(), new HistorySummarizer(chatClient))
        });

        _session = await _agent.CreateSessionAsync(cancellationToken);

        await SubscribeToStreamConsumerInterfaces();

        await RescheduleExistingJobsAsync(cancellationToken);

        await base.OnActivateAsync(cancellationToken);
    }

    public IAsyncEnumerable<string> GetResponseStream(
        string prompt,
        CancellationToken cancellationToken)
    {
        var message = new ChatMessage
        {
            Role = "user",
            Content = prompt,
            Parts = [new ContractsTextContent(prompt)]
        };
        return GetResponseStream(message, cancellationToken);
    }

    public IAsyncEnumerable<string> GetResponseStream(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });
        _currentMessageParts = message.Parts;
        return StreamResponseCore(message.Text, cancellationToken);
    }

    private async IAsyncEnumerable<string> StreamResponseCore(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity(
            $"invoke_agent {this.GetPrimaryKeyString()}", ActivityKind.Server);
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.provider.name", "iaw");
        activity?.SetTag("gen_ai.agent.id", this.GetPrimaryKeyString());
        activity?.SetTag("gen_ai.agent.name", DisplayName);
        activity?.SetTag("gen_ai.conversation.id", this.GetPrimaryKeyString());

        var sw = Stopwatch.StartNew();
        var completed = false;
        try
        {
            var attachmentText = await ResolveAttachments(prompt, cancellationToken);
            var contextBlock = await BuildContextBlock(prompt, cancellationToken);
            _chatOptions!.Instructions = contextBlock.Length > 0
                ? $"{Instructions}\n\n{contextBlock}"
                : Instructions;

            var fullPrompt = attachmentText != prompt ? attachmentText : prompt;

            var channel = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleReader = true });
            _toolProgressWriter = channel.Writer;

            // bare async call, NOT Task.Run — must stay on grain scheduler
            var producerTask = ProduceLlmStreamAsync(fullPrompt, channel.Writer, cancellationToken);

            await foreach (var text in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return text;
                Activity.Current = activity;
            }

            await producerTask;

            if (_usageCapture.LastUsage is { } usage)
            {
                activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
                activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
                RecordTokenMetrics(usage);
            }

            var correlationId = activity?.TraceId.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
            durableState.EventLog.Add(new AgentEvent(
                "LlmCall", this.GetPrimaryKeyString(), correlationId,
                DateTimeOffset.UtcNow, new Dictionary<string, string> { ["prompt_length"] = prompt.Length.ToString() }));

            await WriteStateAsync(cancellationToken);
            completed = true;
        }
        finally
        {
            _toolProgressWriter = null;
            if (!completed)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    activity?.SetTag("gen_ai.stream.cancelled", true);
                }
                else
                {
                    activity?.SetTag("error.type", "conversation_error");
                    AgentTelemetry.ConversationErrors.Add(1, new TagList { { "agent.type", GetType().Name } });
                }
            }
            AgentTelemetry.ConversationDuration.Record(sw.Elapsed.TotalSeconds,
                new TagList { { "agent.type", GetType().Name } });
        }
    }

    private async Task ProduceLlmStreamAsync(
        string prompt, ChannelWriter<string> writer, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _agent!.RunStreamingAsync(
                prompt, _session, cancellationToken: ct))
            {
                if (chunk.Text is { } text)
                    writer.TryWrite(text);
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    public virtual async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in GetResponseStream(prompt, cancellationToken))
            sb.Append(chunk);

        var result = sb.ToString();
        if (result.Length > 8000)
        {
            var truncated = result[..8000];
            var lastNewline = truncated.LastIndexOf('\n');
            if (lastNewline > 6000) truncated = truncated[..lastNewline];
            return truncated + "\n...(output truncated at 8KB)";
        }
        return result;
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ChatMessage> snapshot = durableState.History.ToArray();
        return Task.FromResult(snapshot);
    }

    public async Task ClearHistory(CancellationToken cancellationToken = default)
    {
        durableState.History.Clear();
        await WriteStateAsync(cancellationToken);
        _session = await _agent!.CreateSessionAsync(cancellationToken);
    }

    public Task<AgentUsage?> GetLastUsage(CancellationToken ct = default)
        => Task.FromResult(_usageCapture.LastUsage);

    private void RecordTokenMetrics(AgentUsage usage)
    {
        var tags = new TagList
        {
            { "gen_ai.agent.name", DisplayName },
            { "gen_ai.operation.name", "invoke_agent" }
        };
        var inputTags = new TagList
        {
            { "gen_ai.agent.name", DisplayName },
            { "gen_ai.operation.name", "invoke_agent" },
            { "gen_ai.token.type", "input" }
        };
        var outputTags = new TagList
        {
            { "gen_ai.agent.name", DisplayName },
            { "gen_ai.operation.name", "invoke_agent" },
            { "gen_ai.token.type", "output" }
        };
        AgentTelemetry.TokenUsage.Record(usage.InputTokens, inputTags);
        AgentTelemetry.TokenUsage.Record(usage.OutputTokens, outputTags);
        AgentTelemetry.TotalInputTokens.Add(usage.InputTokens, tags);
        AgentTelemetry.TotalOutputTokens.Add(usage.OutputTokens, tags);
    }

    private async Task<string> BuildContextBlock(string prompt, CancellationToken ct)
    {
        var providers = GetContextProviders();
        if (providers.Count == 0) return "";

        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.enrich_context");
        activity?.SetTag("context.provider_count", providers.Count);

        var contextParts = new List<string>();
        foreach (var provider in providers)
        {
            try
            {
                using var providerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                providerTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                var items = await provider.GetContextAsync(this.GetPrimaryKeyString(), prompt, providerTimeout.Token);
                contextParts.AddRange(items);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                activity?.SetTag($"context.provider_timeout.{provider.Name}", true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetTag("context.provider_error", ex.GetType().Name);
            }
        }

        // Deduplicate exact matches across providers
        contextParts = contextParts.Distinct().ToList();

        activity?.SetTag("context.items_found", contextParts.Count);

        return contextParts.Count > 0
            ? $"[Current context]\n{string.Join("\n", contextParts)}"
            : "";
    }

    private async Task<string> ResolveAttachments(string prompt, CancellationToken ct)
    {
        if (_currentMessageParts is null or { Count: 0 })
            return prompt;

        var blobStorage = ServiceProvider.GetService<BlobFileStorage>();
        if (blobStorage is null) return prompt;

        var attachments = new List<string>();
        foreach (var part in _currentMessageParts)
        {
            if (part is FileContent file)
            {
                try
                {
                    if (file.MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        await using var stream = await blobStorage.DownloadAsync(file.BlobUri);
                        var chunks = await new PdfIngestionSource().ExtractChunksAsync(stream, file.FileName, ct);
                        var text = string.Join("\n", chunks.Select(c => c.Text));
                        attachments.Add($"[Document: {file.FileName}]\n{text}");

                        await IngestChunksAsync(chunks, file, blobStorage, ct);
                    }
                    else
                    {
                        attachments.Add($"[Attached file: {file.FileName} ({file.MimeType}, {file.SizeBytes} bytes)]");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    attachments.Add($"[Attached file: {file.FileName} — could not read content]");
                }
            }
            else if (part is ImageContent image)
            {
                attachments.Add($"[Attached image: {image.Caption ?? "no caption"} ({image.MimeType})]");
            }
        }

        if (attachments.Count == 0) return prompt;
        return $"{string.Join("\n\n", attachments)}\n\n{prompt}";
    }

    private async Task IngestChunksAsync(
        IReadOnlyList<IngestedChunk> chunks, FileContent file, BlobFileStorage blobStorage, CancellationToken ct)
    {
        var embeddingGenerator = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        var qdrantClient = ServiceProvider.GetService<QdrantClient>();
        if (embeddingGenerator is null || qdrantClient is null || chunks.Count == 0) return;

        try
        {
            var projectId = this.GetPrimaryKeyString();
            var collectionName = $"project-{projectId.Replace("/", "-")}";
            var texts = chunks.Select(c => c.Text).ToList();
            var embeddings = await embeddingGenerator.GenerateAsync(texts, cancellationToken: ct);

            var vectorSize = (uint)embeddings[0].Vector.Length;
            var exists = await qdrantClient.CollectionExistsAsync(collectionName, ct);
            if (!exists)
            {
                try
                {
                    await qdrantClient.CreateCollectionAsync(
                        collectionName,
                        new VectorParams { Size = vectorSize, Distance = Distance.Cosine },
                        cancellationToken: ct);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists) { }
            }

            var points = new List<PointStruct>();
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                points.Add(new PointStruct
                {
                    Id = (PointId)Guid.NewGuid(),
                    Vectors = embeddings[i].Vector.ToArray(),
                    Payload =
                    {
                        ["text"] = chunk.Text,
                        ["fileName"] = chunk.FileName,
                        ["pageNumber"] = chunk.PageNumber
                    }
                });
            }

            await qdrantClient.UpsertAsync(collectionName, points, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
        }
    }

    public virtual Task<UIAgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct = default)
        => Task.FromResult(new UIAgentResponse([]));

    public virtual async Task<UIAgentResponse> GetRichResponse(string prompt, CancellationToken ct = default)
    {
        var text = await GetResponse(prompt, ct);
        return new UIAgentResponse([new TextPart(text)]);
    }

    protected static string BuildSafeErrorMessage(Exception ex)
        => $"An error occurred: {ex.GetType().Name} — {ex.Message}";
}
