using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Core;
using IAW.Testing.Scenario;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        _app = await appHost.BuildAsync();

        using var startCts = new CancellationTokenSource(StartupTimeout);
        await _app.StartAsync(startCts.Token);
        await _app.ResourceNotifications
            .WaitForResourceAsync(WaitForResource, KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60), startCts.Token);

        _httpClient = _app.CreateHttpClient(OrleansSiloResource);

        _orleansClientHost = Host.CreateApplicationBuilder()
            .UseOrleansClient(client =>
            {
                client.UseLocalhostClustering();
                client.AddMemoryStreams("agents");
            })
            .Build();

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
}
