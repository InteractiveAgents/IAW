var builder = DistributedApplication.CreateBuilder(args);

var ollama = builder.AddOllama("ollama").WithOpenWebUI().WithGPUSupport().WithDataVolume();
var qwen = ollama.AddModel("qwen2.5");

builder.AddProject<Projects.Sample>("sample");
builder.AddProject<Projects.DevUI>("devui").WithReference(qwen).WaitFor(qwen);

builder.Build().Run();