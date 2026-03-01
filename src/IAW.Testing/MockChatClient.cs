using Microsoft.Extensions.AI;

namespace IAW.Testing;

public sealed class MockChatClient : IChatClient
{
    private readonly List<string> _receivedMessages = [];
    private Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? _responseFactory;
    private Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? _streamFactory;

    public int SendCount { get; private set; }
    public IReadOnlyList<string> ReceivedMessages => _receivedMessages;

    public MockChatClient ReturnsText(string response)
    {
        _responseFactory = (_, _, _) =>
        {
            var chatMessage = new ChatMessage(ChatRole.Assistant, response);
            return Task.FromResult(new ChatResponse(chatMessage));
        };
        return this;
    }

    public MockChatClient ReturnsStream(params string[] chunks)
    {
        _streamFactory = (_, _, ct) => StreamChunksAsync(chunks, ct);
        return this;
    }

    public MockChatClient ThrowsOnSend(Exception exception)
    {
        _responseFactory = (_, _, _) => throw exception;
        _streamFactory = (_, _, _) => ThrowAsync(exception);
        return this;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SendCount++;
        RecordMessages(chatMessages);

        if (_responseFactory is not null)
            return await _responseFactory(chatMessages, options, cancellationToken);

        var message = new ChatMessage(ChatRole.Assistant, string.Empty);
        return new ChatResponse(message);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SendCount++;
        RecordMessages(chatMessages);

        if (_streamFactory is not null)
            return _streamFactory(chatMessages, options, cancellationToken);

        return EmptyStreamAsync();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private void RecordMessages(IEnumerable<ChatMessage> messages)
    {
        foreach (var msg in messages)
        {
            if (msg.Text is { Length: > 0 } text)
                _receivedMessages.Add(text);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamChunksAsync(
        string[] chunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmptyStreamAsync()
    {
        yield break;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowAsync(Exception ex)
    {
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
