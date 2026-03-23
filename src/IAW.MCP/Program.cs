using Aspire.IAW;
using IAW.MCP.Deploy;

var builder = WebApplication.CreateBuilder(args);
builder.AddIAWClient();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<AgentTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp();
app.MapDeployEndpoints();
app.Run();