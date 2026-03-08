using Core.Contracts;
using IAW.Agents.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

// IAW Simple Client — connect to a running cluster and chat with PersonalAssistant
// Run 'aspire run' first, then 'dotnet run --project samples/SimpleClient'

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleansClient(client =>
{
    client.Configure<Orleans.Configuration.ClusterOptions>(options =>
    {
        options.ClusterId = "dev";
        options.ServiceId = "dev";
    });
    client.UseStaticClustering(new IPEndPoint(IPAddress.Loopback, 30_000));
});

var host = builder.Build();
await host.StartAsync(ct);

var grains = host.Services.GetRequiredService<IGrainFactory>();
var agent = grains.GetGrain<IPersonalAssistant>("simple-client-demo");

Console.WriteLine("=== IAW Simple Client ===");
Console.WriteLine();

// Metadata
var metadata = await agent.GetMetadata(ct);
Console.WriteLine($"Agent: {metadata.DisplayName} ({metadata.AgentType}), Kind: {metadata.Kind}");

// Capabilities
var caps = await agent.GetCapabilities(ct);
Console.WriteLine($"Memory={caps.HasMemory} P2P={caps.HasP2P} Events={caps.HasEvents} Tools={caps.HasTools}");
Console.WriteLine();

// Chat
Console.WriteLine("--- Chat ---");
var response = await agent.GetResponse("Hello! Briefly list your team members.", ct);
Console.WriteLine($"PA: {response}");
Console.WriteLine();

// Streaming
Console.WriteLine("--- Streaming ---");
await foreach (var chunk in agent.GetResponseStream("Count from 1 to 5, one number per line.", ct))
    Console.Write(chunk);
Console.WriteLine();
Console.WriteLine();

// History
var history = await agent.GetHistory(ct);
Console.WriteLine($"Conversation history: {history.Count} messages");

await host.StopAsync(ct);
