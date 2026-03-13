using IAW.Agents.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

// IAW Pipeline -- demonstrates agent conversation and event flow
// Run 'aspire run' first, then 'dotnet run --project samples/Pipeline'

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

Console.WriteLine("=== IAW Pipeline ===");
Console.WriteLine();

var producer = grains.GetGrain<IPersonalAssistant>("pipeline-producer");
var consumer = grains.GetGrain<IPersonalAssistant>("pipeline-consumer");

// Step 1: Send tasks to the producer
Console.WriteLine("Sending tasks to producer agent...");
for (var i = 1; i <= 3; i++)
{
    var response = await producer.GetResponse($"Execute pipeline step {i}: process payload {i}.", ct);
    Console.WriteLine($"  Step {i} response: {response[..Math.Min(80, response.Length)]}...");
}

// Step 2: Read producer's event log
var producerEvents = await producer.GetEventLog(ct);
Console.WriteLine($"\nProducer event log: {producerEvents.Count} events");
foreach (var e in producerEvents)
    Console.WriteLine($"  {e.EventName} @ {e.Timestamp:HH:mm:ss}");

// Step 3: Have consumer summarize the pipeline result
var summary = await consumer.GetResponse("Summarize what just happened in one sentence.", ct);
Console.WriteLine($"\nConsumer summary: {summary}");

Console.WriteLine("\nPipeline complete!");
await host.StopAsync(ct);
