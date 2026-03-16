using Aspire.IAW;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);
builder.AddIAW();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapGet("/", () => "IAW Assistant Silo");
app.Run();
