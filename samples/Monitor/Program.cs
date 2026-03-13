using IAW.Agents.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

// IAW Monitor — demonstrates agent state, workspace, and monitoring events
// Run 'aspire run' first, then 'dotnet run --project samples/Monitor'

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
var agent = grains.GetGrain<IPersonalAssistant>("monitor-demo");

Console.WriteLine("=== IAW Monitor ===");
Console.WriteLine();

// 1. Set workspace
Console.WriteLine("Setting workspace...");
await agent.SetWorkspace("/tmp/monitored-project", ct);

var state = await agent.GetState(ct);
Console.WriteLine($"State entries: {state.Entries.Count}");
foreach (var kvp in state.Entries)
    Console.WriteLine($"  {kvp.Key} = {kvp.Value.Value}");
Console.WriteLine();

// 2. Trigger monitoring via agent conversation (events published internally)
Console.WriteLine("Triggering monitoring checks...");
await agent.GetResponse("Run a health check on /tmp/monitored-project and report status.", ct);
Console.WriteLine("  Health check triggered.");

await agent.GetResponse("Alert: Disk usage above 80% on /tmp/monitored-project.", ct);
Console.WriteLine("  Alert triggered.");

// 3. Review event log
var events = await agent.GetEventLog(ct);
Console.WriteLine($"\nEvent log ({events.Count} events):");
foreach (var e in events)
{
    var data = string.Join(", ", e.Payload.Select(kv => $"{kv.Key}={kv.Value}"));
    Console.WriteLine($"  [{e.EventName}] {e.Timestamp:HH:mm:ss} — {data}");
}

// 4. Check subscriptions
var subs = await agent.GetActiveSubscriptions(ct);
Console.WriteLine($"\nActive subscriptions: {subs.Count}");

// 5. Get capabilities
var caps = await agent.GetCapabilities(ct);
Console.WriteLine($"Capabilities: Memory={caps.HasMemory} Events={caps.HasEvents} Tools={caps.HasTools}");

// 6. Cancel (stop monitoring)
await agent.Cancel(ct);
Console.WriteLine("\nMonitor stopped.");

await host.StopAsync(ct);
