using System.Text;
using Microsoft.AI.Foundry.Local;

using Core.AI;

namespace TelegramClient.Services;

public sealed class FoundryLocalTranscriptionService : IAudioTranscriptionService
{
    private static readonly string[] OggExtensions = [".ogg", ".opus", ".oga"];

    private readonly IConfiguration _configuration;
    private readonly IAudioConverter? _audioConverter;
    private readonly ILogger<FoundryLocalTranscriptionService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Model? _model;
    private bool _initialized;

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
        await EnsureInitializedAsync(ct);

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
            _logger.LogDebug("Transcribing: {Path}", fileToTranscribe);
            var client = await _model!.GetAudioClientAsync();
            var result = new StringBuilder();

            await foreach (var chunk in client.TranscribeAudioStreamingAsync(fileToTranscribe, ct))
                result.Append(chunk.Text);

            var transcription = result.ToString().Trim();
            _logger.LogInformation("Transcription complete: {Length} chars", transcription.Length);
            return transcription;
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

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            _logger.LogInformation("Initializing Foundry Local for Whisper...");

            try { _ = FoundryLocalManager.Instance; }
            catch (FoundryLocalException)
            {
                var config = new Configuration { AppName = "iaw" };
                await FoundryLocalManager.CreateAsync(config, _logger);
            }

            var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);
            _model = await ResolveModelAsync(catalog);

            await _model.DownloadAsync();
            await _model.LoadAsync();

            _initialized = true;
            _logger.LogInformation("Whisper model loaded: {ModelId}", _model.Id);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<Model> ResolveModelAsync(ICatalog catalog)
    {
        var configuredId = _configuration[LlmConfig.WhisperModelId];

        if (!string.IsNullOrEmpty(configuredId))
        {
            var configured = await catalog.GetModelAsync(configuredId);
            if (configured is not null) return configured;
            _logger.LogWarning("Configured whisper model '{ModelId}' not found, falling back", configuredId);
        }

        foreach (var whisperModel in WhisperModel.All.OrderByDescending(m => m.Priority))
        {
            var found = await catalog.GetModelAsync(whisperModel.Id);
            if (found is not null) return found;
        }

        throw new InvalidOperationException(
            "No whisper model found in Foundry Local catalog. " +
            $"Expected one of: {string.Join(", ", WhisperModel.All.Select(m => m.Id))}");
    }

    public async ValueTask DisposeAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized && _model is not null)
            {
                _logger.LogInformation("Unloading Whisper model {ModelId}", _model.Id);
                await _model.UnloadAsync();
            }
        }
        finally
        {
            _initLock.Release();
            _initLock.Dispose();
        }
    }
}
