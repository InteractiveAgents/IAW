using Core.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.IAW;

internal sealed class WhisperHealthCheck(IAudioTranscriptionService transcriptionService) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (transcriptionService is not IWhisperReadiness readiness)
            return Task.FromResult(HealthCheckResult.Degraded("Transcription service does not report readiness"));

        if (readiness.IsReady)
            return Task.FromResult(HealthCheckResult.Healthy("Whisper model loaded"));

        if (readiness.InitializationFailed)
            return Task.FromResult(HealthCheckResult.Unhealthy($"Whisper initialization failed: {readiness.ErrorMessage}"));

        return Task.FromResult(HealthCheckResult.Degraded("Whisper model still initializing"));
    }
}
