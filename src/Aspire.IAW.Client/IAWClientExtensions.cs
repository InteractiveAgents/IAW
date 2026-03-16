using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Dashboard;
using Orleans.Journaling;
using Core.AI;
using Core.Services;

namespace Aspire.IAW;

public static class IAWClientExtensions
{
    public static TBuilder AddIAW<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.UseOrleans(silo =>
        {
            silo.Configure<Orleans.Configuration.EndpointOptions>(ep =>
                ep.AdvertisedIPAddress = System.Net.IPAddress.Loopback);
            silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
            silo.AddStateMachineStorage();
            silo.AddDashboard();
        });

        builder.AddLlmProviders();
        builder.AddEmbeddingProvider();

        builder.AddAzureBlobServiceClient("file-storage");
        builder.AddQdrantClient("qdrant");
        builder.Services.AddSingleton<BlobFileStorage>();

        return builder;
    }

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

        builder.UseOrleansClient(client => client.UseLocalhostClustering());

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
