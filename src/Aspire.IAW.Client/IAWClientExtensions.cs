using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Core.AI;

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
        builder.UseOrleansClient(client => client.UseLocalhostClustering(clusterId: clusterId, serviceId: serviceId));

        return builder;
    }

    public static IHostApplicationBuilder AddWhisperProvider<TService>(this IHostApplicationBuilder builder)
        where TService : class, IAudioTranscriptionService
    {
        builder.Services.AddSingleton<IAudioTranscriptionService, TService>();
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
