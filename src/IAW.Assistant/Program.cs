using Aspire.IAW;
using Orleans.Dashboard;

using Core.Registry;

Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);
builder.AddIAW();
builder.UseOrleans(silo => silo.AddStartupTask<AgentRegistrationStartupTask>());

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapGet("/", () => "IAW Assistant Silo");
app.Run();
