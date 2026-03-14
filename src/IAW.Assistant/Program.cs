using Core.AI;
using Core.Services;
using Orleans.Dashboard;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddIAW();
builder.AddLlmProviders();
builder.AddEmbeddingProvider();
builder.AddAzureBlobServiceClient("file-storage");
builder.AddQdrantClient("qdrant");
builder.Services.AddSingleton<BlobFileStorage>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapGet("/", () => "IAW Assistant Silo");

app.Run();
