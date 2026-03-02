using System.Net;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Core;
using IAW.Testing.Scenario;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Xunit;

namespace IAW.Testing;

public abstract class AspireAgentTest<T> : IAsyncLifetime where T : class, IAgent
{
    private DistributedApplication _app = null!;
    private IHost _orleansClientHost = null!;
    private IClusterClient _orleansClient = null!;
    private HttpClient _httpClient = null!;

    protected DistributedApplication App => _app;
    protected IClusterClient OrleansClient => _orleansClient;
    protected HttpClient HttpClient => _httpClient;
    protected ScenarioBuilder Scenario => new(id => _orleansClient.GetGrain<IAgent>(id));

    protected IAgent Agent(string id) => _orleansClient.GetGrain<IAgent>(id);

    protected virtual string[] AppHostArgs =>
    [
        "--Parameters:anthropic-api-key=test-key",
        "--Parameters:github-token=test-token",
        "--Parameters:ngrok-auth-token=test-ngrok",
        "--Parameters:bot-token=test-bot"
    ];
    protected virtual string WaitForResource => "samples";
    protected virtual string OrleansSiloResource => "samples";
    protected virtual TimeSpan StartupTimeout => TimeSpan.FromMinutes(3);

    protected virtual Task OnAppStartedAsync() => Task.CompletedTask;
    protected virtual Task OnBeforeTestAsync() => Task.CompletedTask;
    protected virtual Task OnAfterTestAsync() => Task.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire>(AppHostArgs);

        // Orleans TCP endpoints can't go through Aspire's HTTP proxy.
        // Disable proxying and assign available ports so the test client
        // can connect directly to the silo gateway.
        DisableOrleansProxy(appHost);

        _app = await appHost.BuildAsync();

        using var startCts = new CancellationTokenSource(StartupTimeout);
        await _app.StartAsync(startCts.Token);
        await _app.ResourceNotifications
            .WaitForResourceAsync(WaitForResource, KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60), startCts.Token);

        _httpClient = _app.CreateHttpClient(OrleansSiloResource);

        var gatewayEndpoint = _app.GetEndpoint(OrleansSiloResource, "orleans-gateway");
        var gatewayPort = gatewayEndpoint.Port;

        var clientHostBuilder = Host.CreateApplicationBuilder();
        clientHostBuilder.UseOrleansClient(client =>
        {
            client.UseStaticClustering(new IPEndPoint(IPAddress.Loopback, gatewayPort));
            client.AddMemoryStreams("agents");
        });
        // Orleans BindConfiguration("Orleans") overrides UseLocalhostClustering's ClusterOptions.
        // In production, Aspire injects Orleans__ClusterId env var so BindConfiguration reads "dev".
        // In tests, the client host has no Aspire env vars — PostConfigure guarantees the match.
        clientHostBuilder.Services.PostConfigure<ClusterOptions>(options =>
        {
            options.ClusterId = "dev";
            options.ServiceId = "dev";
        });
        _orleansClientHost = clientHostBuilder.Build();

        await _orleansClientHost.StartAsync(startCts.Token);
        _orleansClient = _orleansClientHost.Services.GetRequiredService<IClusterClient>();

        await OnAppStartedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        await _orleansClientHost.StopAsync();
        _orleansClientHost.Dispose();
        await _app.DisposeAsync();
    }

    private static void DisableOrleansProxy(IDistributedApplicationTestingBuilder builder)
    {
        foreach (var resource in builder.Resources)
        {
            foreach (var endpoint in resource.Annotations.OfType<EndpointAnnotation>())
            {
                if (endpoint.Name is "orleans-silo" or "orleans-gateway")
                {
                    endpoint.IsProxied = false;
                    endpoint.Port = GetAvailablePort();
                }
            }
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
