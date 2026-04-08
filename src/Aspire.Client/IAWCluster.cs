using Core;
using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aspire.IAW;

// Simplified entry point for orchestration scripts: using var iaw = await IAWCluster.Connect(args);
public sealed class IAWCluster : IAsyncDisposable
{
    readonly IHost _host;

    IAWCluster(IHost host, IClusterClient client)
    {
        _host = host;
        Client = client;
        TaskId = "task-" + Guid.NewGuid().ToString("N");
    }

    public IClusterClient Client { get; }
    public string TaskId { get; }

    public static async Task<IAWCluster> Connect(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddIAWClient();
        var host = builder.Build();
        await host.StartAsync();
        var client = host.Services.GetRequiredService<IClusterClient>();
        return new IAWCluster(host, client);
    }

    public T Get<T>() where T : IAgent => Client.Get<T>();
    public T Get<T>(string scope) where T : IAgent => Client.Get<T>(scope);

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}