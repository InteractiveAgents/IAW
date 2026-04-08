using Core.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aspire.IAW;

// Orleans client configuration. Called by MCP, DevUI, Telegram (grain consumers).
// For silo configuration, see IAWSiloExtensions.cs.
public static class IAWClientExtensions
{
    public static TBuilder AddIAWClient<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        var clusterId = builder.Configuration["Orleans:ClusterId"] ?? "dev";
        var serviceId = builder.Configuration["Orleans:ServiceId"] ?? "dev";
        builder.UseOrleansClient(client =>
        {
            client.UseLocalhostClustering(clusterId: clusterId, serviceId: serviceId);
            client.Configure<Orleans.Configuration.ClientMessagingOptions>(msg =>
                msg.ResponseTimeout = TimeSpan.FromMinutes(5));
        });

        // Retry gateway connection until the silo is ready
        builder.Services.AddSingleton<IClientConnectionRetryFilter, GatewayConnectionRetryFilter>();

        return builder;
    }

    public static IHostApplicationBuilder AddWhisperProvider<TService>(this IHostApplicationBuilder builder)
        where TService : class, IAudioTranscriptionService, IHostedService
    {
        builder.Services.AddSingleton<TService>();
        builder.Services.AddSingleton<IAudioTranscriptionService>(sp => sp.GetRequiredService<TService>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TService>());
        builder.Services.AddHealthChecks()
            .AddCheck<WhisperHealthCheck>("whisper", tags: ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");

            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}

sealed class GatewayConnectionRetryFilter(ILogger<GatewayConnectionRetryFilter> logger) : IClientConnectionRetryFilter
{
    public async Task<bool> ShouldRetryConnectionAttempt(Exception exception, CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "Orleans gateway connection failed, retrying in 2s...");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return true;
    }
}