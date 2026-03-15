using System.ClientModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;

namespace Core.AI;

public sealed class FoundryLocalTranscriptionService : IAudioTranscriptionService
{
    private static readonly string[] OggExtensions = [".ogg", ".opus", ".oga"];

    private readonly IConfiguration _configuration;
    private readonly IAudioConverter? _audioConverter;
    private readonly ILogger<FoundryLocalTranscriptionService> _logger;

    public FoundryLocalTranscriptionService(
        IConfiguration configuration,
        ILogger<FoundryLocalTranscriptionService> logger,
        IAudioConverter? audioConverter = null)
    {
        _configuration = configuration;
        _logger = logger;
        _audioConverter = audioConverter;
    }

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);

        var extension = Path.GetExtension(audioFilePath).ToLowerInvariant();
        var fileToTranscribe = audioFilePath;
        string? convertedFilePath = null;

        if (OggExtensions.Contains(extension) && _audioConverter is not null)
        {
            _logger.LogDebug("Converting {Source} to WAV", audioFilePath);
            convertedFilePath = _audioConverter.ConvertToWav(audioFilePath);
            fileToTranscribe = convertedFilePath;
        }

        try
        {
            var endpoint = _configuration[LlmConfig.WhisperEndpoint]
                ?? ResolveEndpointFromConnectionString()
                ?? throw new InvalidOperationException(
                    $"Whisper endpoint not configured. Set {LlmConfig.WhisperEndpoint} or add .WithVoice2Text() in AppHost.");

            var modelId = _configuration[LlmConfig.WhisperModelId] ?? "whisper-large-v3-turbo";

            var client = new AudioClient(modelId,
                new ApiKeyCredential("not-required"),
                new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            _logger.LogDebug("Transcribing {Path} via {Endpoint}", fileToTranscribe, endpoint);

            await using var audioStream = File.OpenRead(fileToTranscribe);
            var transcription = await client.TranscribeAudioAsync(
                audioStream, Path.GetFileName(fileToTranscribe), cancellationToken: ct);

            var text = transcription.Value.Text?.Trim() ?? "";
            _logger.LogInformation("Transcription complete: {Length} chars", text.Length);
            return text;
        }
        finally
        {
            if (convertedFilePath is not null && File.Exists(convertedFilePath))
                File.Delete(convertedFilePath);
        }
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        var tempPath = Path.Combine(Path.GetTempPath(), $"iaw_voice_{Guid.NewGuid()}{Path.GetExtension(fileName)}");
        try
        {
            await using (var fileStream = File.Create(tempPath))
                await audioStream.CopyToAsync(fileStream, ct);
            return await TranscribeAsync(tempPath, ct);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private string? ResolveEndpointFromConnectionString()
    {
        var connectionString = _configuration["ConnectionStrings:whisper"]
            ?? _configuration["ConnectionStrings:foundry"];
        if (string.IsNullOrEmpty(connectionString)) return null;

        // Aspire connection strings can be "Endpoint=http://..." or just a URL
        if (connectionString.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
            return connectionString.Split(';')[0]["Endpoint=".Length..];
        if (connectionString.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return connectionString;
        return null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
