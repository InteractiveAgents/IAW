using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddIAWClient();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<AgentTools>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp();

app.Run();
