var builder = DistributedApplication.CreateBuilder(args);

var ollama = builder.AddOllama("ollama").WithOpenWebUI().WithGPUSupport();

ollama.AddModel("phi4");

builder.AddProject<Projects.Sample_Silo>("sample-silo");

builder.Build().Run();
