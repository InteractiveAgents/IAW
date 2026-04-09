using System.Collections.Concurrent;

namespace TelegramClient.Services;

// per-chat rate limiter to respect Telegram's 30 msg/sec limit
// uses a simple sliding-window token bucket per chat
public sealed class TelegramRateLimiter
{
    const int MaxTokensPerSecond = 25; // below 30 limit for safety margin
    const int WindowMs = 1000;

    readonly ConcurrentDictionary<long, ChatBucket> _buckets = new();

    public async Task AcquireAsync(long chatId, CancellationToken ct = default)
    {
        var bucket = _buckets.GetOrAdd(chatId, _ => new ChatBucket());
        await bucket.WaitAsync(ct);
    }

    sealed class ChatBucket
    {
        readonly SemaphoreSlim _semaphore = new(MaxTokensPerSecond, MaxTokensPerSecond);

        public async Task WaitAsync(CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);

            // release the token after the window expires
            _ = Task.Delay(WindowMs, ct).ContinueWith(_ =>
            {
                try { _semaphore.Release(); } catch (ObjectDisposedException) { }
            }, TaskScheduler.Default);
        }
    }
}
