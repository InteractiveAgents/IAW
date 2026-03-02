namespace TelegramBot.Services;

public interface IVoiceTranscriptionService
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
}

public sealed class VoiceTranscriptionService(ILogger<VoiceTranscriptionService> logger) : IVoiceTranscriptionService
{
    public Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        // Foundry Local Whisper integration pending
        logger.LogWarning("Voice transcription not yet configured — Foundry Local Whisper integration pending");
        return Task.FromResult(string.Empty);
    }
}
