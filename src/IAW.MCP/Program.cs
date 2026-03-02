using System.Net;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var gatewayAddress = builder.Configuration["Orleans:PrimaryGateway"];

builder.UseOrleansClient(client =>
{
    if (!string.IsNullOrEmpty(gatewayAddress))
    {
        var uri = new Uri(gatewayAddress);
        client.UseStaticClustering(new IPEndPoint(IPAddress.Loopback, uri.Port));
    }
    else
    {
        client.UseLocalhostClustering();
    }
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<AgentTools>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp();

app.Run();
