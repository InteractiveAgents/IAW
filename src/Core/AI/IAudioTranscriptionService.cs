namespace Core.AI;

public interface IAudioTranscriptionService : IAsyncDisposable
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
    Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default);
}

public interface IWhisperReadiness
{
    bool IsReady { get; }
    bool InitializationFailed { get; }
    string? ErrorMessage { get; }
}