using Core;
using Core.AI;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.E2E.Tests;

public class CodeOrchestrationE2ETests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private static bool _ollamaAvailable = CheckOllama();

    static bool CheckOllama()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var endpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
            var response = http.GetAsync(endpoint).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async ValueTask InitializeAsync()
    {
        if (!_ollamaAvailable)
        {
            // Skip cluster setup — tests will skip via Skip.If
            return;
        }

        Environment.SetEnvironmentVariable("IAW__Workspace", "D:\\IAW-E2E-Workspace");

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<OllamaSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_ollamaAvailable) return;

        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
        Environment.SetEnvironmentVariable("IAW__Workspace", null);

        if (Directory.Exists("D:\\IAW-E2E-Workspace\\tasks"))
            Directory.Delete("D:\\IAW-E2E-Workspace\\tasks", recursive: true);
    }

    [Fact(Timeout = 300_000)]
    [Trait("Category", "E2E")]
    public async Task Execute_CreatesHelloWorldProject()
    {
        Assert.SkipWhen(!_ollamaAvailable, "Ollama not available at localhost:11434");
        var ct = TestContext.Current.CancellationToken;
        var testId = Guid.NewGuid().ToString("N")[..8];

        var project = _cluster.GrainFactory.GetGrain<IThread>($"e2e-{testId}/general");

        var response = await project.GetResponse(
            $"Create a C# console app at D:/E2ETest_{testId} that prints Hello World", ct);

        Assert.NotNull(response);
        Assert.NotEmpty(response);

        var tasksDir = "D:\\IAW-E2E-Workspace\\tasks";
        Assert.True(Directory.Exists(tasksDir),
            $"Workspace tasks dir missing. Response: {response[..Math.Min(500, response.Length)]}");

        var taskDirs = Directory.GetDirectories(tasksDir)
            .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
            .ToArray();
        Assert.NotEmpty(taskDirs);

        var latestTask = taskDirs[0];
        Assert.True(File.Exists(Path.Combine(latestTask, "plan.md")), "plan.md missing");
        Assert.True(File.Exists(Path.Combine(latestTask, "orchestration.cs")), "orchestration.cs missing");
        Assert.True(File.Exists(Path.Combine(latestTask, "log.txt")), "log.txt missing");

        var resultPath = Path.Combine(latestTask, "result.json");
        if (File.Exists(resultPath))
        {
            var resultJson = await File.ReadAllTextAsync(resultPath, ct);
            Assert.Contains("success", resultJson, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(Timeout = 120_000)]
    [Trait("Category", "E2E")]
    public async Task Thread_AnswersSimpleQuestionDirectly()
    {
        Assert.SkipWhen(!_ollamaAvailable, "Ollama not available at localhost:11434");
        var ct = TestContext.Current.CancellationToken;
        var testId = Guid.NewGuid().ToString("N")[..8];

        var project = _cluster.GrainFactory.GetGrain<IThread>($"e2e-{testId}/general");

        var response = await project.GetResponse("What is 2+2?", ct);

        Assert.NotNull(response);
        Assert.Contains("4", response);
    }
}

public class OllamaSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams(IAWConstants.StreamProvider)
            .UseInMemoryReminderService();

        siloBuilder.Services.AddSingleton<Orleans.Journaling.IStateMachineStorageProvider,
            Orleans.Journaling.VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();

        // Use REAL Ollama LLM instead of mock
        var ollamaEndpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
        IChatClient ollamaClient = new OllamaSharp.OllamaApiClient(new Uri(ollamaEndpoint), "qwen2.5");

        // Register for all model attribute mappers (Sonnet46, Claude45Haiku, etc. all get Ollama)
        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(siloBuilder.Services, ollamaClient);
        siloBuilder.Services.AddSingleton<IChatClient>(ollamaClient);

        // Mock embedding generator (no Qdrant in tests)
        siloBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new MockEmbeddingGenerator());

        siloBuilder.Services.AddHttpClient();
    }
}