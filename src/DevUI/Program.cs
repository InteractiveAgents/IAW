using DevUI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using ServiceDefaults;
using System.Net;

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

builder.Services.AddSingleton<IChatClient, OrleansAgentChatClient>();

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