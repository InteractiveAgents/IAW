using Core.AI;
using Microsoft.AI.Foundry.Local;
using System.Text;

namespace TelegramClient.Services;

public sealed class FoundryLocalTranscriptionService : IAudioTranscriptionService, IHostedService, IWhisperReadiness
{
    private static readonly string[] OggExtensions = [".ogg", ".opus", ".oga"];
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

    private readonly IConfiguration _configuration;
    private readonly IAudioConverter? _audioConverter;
    private readonly ILogger<FoundryLocalTranscriptionService> _logger;
    private Model? _model;

    public bool IsReady { get; private set; }
    public bool InitializationFailed { get; private set; }
    public string? ErrorMessage { get; private set; }

    public FoundryLocalTranscriptionService(
        IConfiguration configuration,
        ILogger<FoundryLocalTranscriptionService> logger,
        IAudioConverter? audioConverter = null)
    {
        _configuration = configuration;
        _logger = logger;
        _audioConverter = audioConverter;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Initializing Foundry Local for Whisper...");
            await InitializeFoundryManagerAsync(ct);

            var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);
            _model = await ResolveModelAsync(catalog);

            _logger.LogInformation("Downloading Whisper model {ModelId}...", _model.Id);
            await WithTimeout(_model.DownloadAsync(), DownloadTimeout, "Whisper model download", ct);

            _logger.LogInformation("Loading Whisper model {ModelId}...", _model.Id);
            await WithTimeout(_model.LoadAsync(), LoadTimeout, "Whisper model load", ct);

            IsReady = true;
            _logger.LogInformation("Whisper model ready: {ModelId}", _model.Id);
        }
        catch (Exception ex)
        {
            InitializationFailed = true;
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Whisper initialization failed");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (IsReady && _model is not null)
        {
            _logger.LogInformation("Unloading Whisper model {ModelId}", _model.Id);
            try { await _model.UnloadAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error unloading Whisper model"); }
        }
    }

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        EnsureReady();

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

    public async ValueTask DisposeAsync()
    {
        if (IsReady && _model is not null)
        {
            try { await _model.UnloadAsync(); }
            catch { /* best-effort cleanup */ }
        }
    }

    private void EnsureReady()
    {
        if (IsReady) return;
        if (InitializationFailed)
            throw new InvalidOperationException($"Whisper is not available: {ErrorMessage}");
        throw new InvalidOperationException("Whisper model is still initializing");
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != task)
            throw new TimeoutException($"{operation} timed out after {timeout.TotalMinutes:F0} minutes");
        await task; // propagate any exception
    }

    private async Task InitializeFoundryManagerAsync(CancellationToken ct)
    {
        try
        {
            _ = FoundryLocalManager.Instance;
            _logger.LogDebug("Foundry Local manager already initialized");
        }
        catch (FoundryLocalException)
        {
            _logger.LogInformation("Creating new Foundry Local manager...");
            var config = new Configuration { AppName = "iaw" };
            await FoundryLocalManager.CreateAsync(config, _logger);
        }
    }

    private async Task<Model> ResolveModelAsync(ICatalog catalog)
    {
        var configuredId = _configuration[LlmConfig.WhisperModelId];

        if (!string.IsNullOrEmpty(configuredId))
        {
            var configured = await catalog.GetModelAsync(configuredId);
            if (configured is not null)
            {
                _logger.LogInformation("Using configured Whisper model: {ModelId}", configuredId);
                return configured;
            }
            _logger.LogWarning("Configured whisper model '{ModelId}' not found in catalog, falling back", configuredId);
        }

        foreach (var whisperModel in WhisperModel.All.OrderByDescending(m => m.Priority))
        {
            var found = await catalog.GetModelAsync(whisperModel.Id);
            if (found is not null)
            {
                _logger.LogInformation("Resolved Whisper model via fallback: {ModelId}", found.Id);
                return found;
            }
        }

        throw new InvalidOperationException(
            "No whisper model found in Foundry Local catalog. " +
            $"Expected one of: {string.Join(", ", WhisperModel.All.Select(m => m.Id))}");
    }
}
