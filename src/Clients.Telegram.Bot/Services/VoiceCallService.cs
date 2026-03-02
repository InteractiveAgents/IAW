namespace TelegramBot.Services;

public interface IVoiceCallService
{
    Task<byte[]> ProcessAudioAsync(byte[] inputAudio, string? persona = null, CancellationToken ct = default);
}

public sealed class VoiceCallService(ILogger<VoiceCallService> logger) : IVoiceCallService
{
    public Task<byte[]> ProcessAudioAsync(byte[] inputAudio, string? persona = null, CancellationToken ct = default)
    {
        // PersonaPlex integration pending
        logger.LogWarning("VoiceCallService is a placeholder — PersonaPlex integration pending");
        return Task.FromResult(Array.Empty<byte>());
    }
}
