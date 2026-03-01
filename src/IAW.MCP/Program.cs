using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.UseOrleansClient(client =>
{
    client.AddMemoryStreams("agents");
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<AgentTools>();

await builder.Build().RunAsync();
