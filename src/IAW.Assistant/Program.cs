using Core.AI;
using Orleans.Dashboard;
using Orleans.Journaling;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

var siloPort = builder.Configuration.GetValue("Orleans:Endpoints:SiloPort", 11_111);
var gatewayPort = builder.Configuration.GetValue("Orleans:Endpoints:GatewayPort", 30_000);
var clusterId = builder.Configuration.GetValue("Orleans:ClusterId", "dev");
var serviceId = builder.Configuration.GetValue("Orleans:ServiceId", "dev");

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering(
        siloPort: siloPort,
        gatewayPort: gatewayPort,
        serviceId: serviceId,
        clusterId: clusterId);
    silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
    silo.AddStateMachineStorage();
    silo.AddDashboard();
});

builder.AddLlmProviders();
builder.AddEmbeddingProvider();
builder.AddServiceDefaults();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapGet("/", () => "IAW Assistant Silo");

app.Run();
