using Azure.Storage.Blobs;
using Core;
using Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Dashboard;
using Orleans.Journaling;

namespace Aspire.IAW;

// Silo-only configuration. Called by IAW.Assistant (the grain host).
// For Orleans client configuration, see IAWClientExtensions.cs.
public static class IAWSiloExtensions
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
            silo.Configure<Orleans.Configuration.SiloMessagingOptions>(msg =>
                msg.ResponseTimeout = TimeSpan.FromMinutes(5));
            silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
            silo.AddStateMachineStorage();
            silo.AddDashboard();
            silo.AddBroadcastChannel(IAWConstants.UIBroadcastProvider);
            silo.UseAzureBlobDurableJobs(optionsBuilder =>
            {
                optionsBuilder.Configure<IServiceProvider>((options, sp) =>
                {
                    var blobClient = sp.GetService<BlobServiceClient>();
                    if (blobClient is not null)
                        options.BlobServiceClient = blobClient;
                    options.ContainerName = "durable-jobs";
                });
            });
        });

        builder.AddLlmProviders();
        builder.AddEmbeddingProvider();

        builder.AddAzureBlobServiceClient("file-storage");
        builder.AddQdrantClient("qdrant");
        builder.Services.AddSingleton<BlobFileStorage>();

        return builder;
    }
}