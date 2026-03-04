using OpenAI.Audio;

namespace TelegramBot.Services;

public interface IVoiceTranscriptionService
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
}

public sealed class VoiceTranscriptionService(
    IConfiguration configuration,
    ILogger<VoiceTranscriptionService> logger) : IVoiceTranscriptionService
{
    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        var apiKey = configuration["AI:LLM:OpenAiApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("OpenAI API key not configured — voice transcription unavailable");
            return "[Voice message received but transcription is not configured]";
        }

        try
        {
            var audioClient = new AudioClient("whisper-1", apiKey);
            await using var audioStream = File.OpenRead(audioFilePath);
            var fileName = Path.GetFileName(audioFilePath);
            var transcription = await audioClient.TranscribeAudioAsync(audioStream, fileName, cancellationToken: ct);
            var text = transcription.Value.Text;

            logger.LogInformation("Transcribed voice message ({Length} chars) from {FilePath}", text.Length, audioFilePath);
            return text;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Voice transcription failed for {FilePath}", audioFilePath);
            return "[Voice message received but transcription failed]";
        }
    }
}
