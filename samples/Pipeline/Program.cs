using Core.Contracts;
using IAW.Agents.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

// IAW Pipeline — demonstrates event publishing and agent event flow
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

// Step 1: Publish events to the producer's stream
Console.WriteLine("Publishing events to producer stream...");
for (var i = 1; i <= 3; i++)
{
    var evt = new AgentEvent(
        $"pipeline.step-{i}", "pipeline-producer", Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow, new Dictionary<string, object>
        {
            ["step"] = i,
            ["data"] = $"Payload from step {i}"
        });
    await producer.PublishToStream(evt, ct);
    Console.WriteLine($"  Published: pipeline.step-{i}");
}

// Step 2: Read producer's event log
var producerEvents = await producer.GetEventLog(ct);
Console.WriteLine($"\nProducer event log: {producerEvents.Count} events");
foreach (var e in producerEvents)
    Console.WriteLine($"  {e.EventName} @ {e.Timestamp:HH:mm:ss}");

// Step 3: Have consumer handle an event from the producer
var resultEvent = new AgentEvent(
    "pipeline.result", "pipeline-consumer", Guid.NewGuid().ToString("N"),
    DateTimeOffset.UtcNow, new Dictionary<string, object>
    {
        ["source"] = "pipeline-producer",
        ["result"] = "Pipeline complete"
    });
await consumer.HandleEvent(resultEvent, ct);

var consumerEvents = await consumer.GetEventLog(ct);
Console.WriteLine($"\nConsumer event log: {consumerEvents.Count} events");
foreach (var e in consumerEvents)
    Console.WriteLine($"  {e.EventName} @ {e.Timestamp:HH:mm:ss}");

// Step 4: Ask consumer to summarize the pipeline result
var summary = await consumer.GetResponse("Summarize what just happened in one sentence.", ct);
Console.WriteLine($"\nConsumer summary: {summary}");

Console.WriteLine("\nPipeline complete!");
await host.StopAsync(ct);
