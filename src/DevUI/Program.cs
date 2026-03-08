using DevUI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using ServiceDefaults;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var gatewayAddress = builder.Configuration["Orleans:PrimaryGateway"];

var clusterId = builder.Configuration.GetValue("Orleans:ClusterId", "dev");
var serviceId = builder.Configuration.GetValue("Orleans:ServiceId", "dev");

builder.UseOrleansClient(client =>
{
    client.Configure<Orleans.Configuration.ClusterOptions>(options =>
    {
        options.ClusterId = clusterId;
        options.ServiceId = serviceId;
    });

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

builder.Services.AddSingleton<IChatClient, OrleansAgentChatClient>();

// Discover all IAgent grain interfaces from loaded assemblies and register with DevUI
AgentDiscovery.DiscoverAndRegisterAgents(builder);

builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();
app.UseHttpsRedirection();
app.MapDefaultEndpoints();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (builder.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.Run();
