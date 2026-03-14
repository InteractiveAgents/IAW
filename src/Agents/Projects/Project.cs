using System.ComponentModel;
using Core.AI;
using Core.AI.Models;
using Core.Context;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

namespace IAW.Agents.Projects;

[GrainType("project-v1")]
public class Project(
    [ProjectState] ProjectDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient), IProject
{
    protected override string Instructions => """
        You are a project assistant. Help the user manage their project,
        answer questions, and coordinate tasks.
        Be concise and actionable in your responses.
        """;
    protected override string DisplayName => "Project";

    protected override IReadOnlyList<IAgentContextProvider> GetContextProviders()
    {
        var qdrant = ServiceProvider.GetService<QdrantClient>();
        var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (qdrant is null || embeddings is null) return [];
        return [new RAGContextProvider(qdrant, embeddings)];
    }

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(RequestApprovalTool, nameof(RequestApprovalTool),
                "Ask the user to approve or decline something. Returns when approval is requested."),
        ];
    }

    [Description("Request user approval with a question and a set of options")]
    private async Task<string> RequestApprovalTool(
        [Description("The question to ask the user")] string question,
        [Description("Available options for the user to choose from")] string[] options)
    {
        var approvalId = Guid.NewGuid().ToString("N")[..8];
        await PublishAsync("approval.requested", new Dictionary<string, object>
        {
            ["approvalId"] = approvalId,
            ["question"] = question,
            ["options"] = options,
            ["projectSlug"] = this.GetPrimaryKeyString()
        });
        return $"Approval requested (id: {approvalId}). Waiting for user response.";
    }

    public Task<ProjectDashboard> GetDashboard(CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task<ProjectTask> AddTask(string description, TaskPriority priority, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectTask>>([]);

    public Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task CancelJob(string jobId, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task RegisterFile(FileReference fileRef, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 3");

    public async Task RequestApproval(string question, string[] options, CancellationToken ct)
    {
        await RequestApprovalTool(question, options);
    }

    public Task<ProjectContext> GetProjectContext(CancellationToken ct) =>
        Task.FromResult(new ProjectContext());
}
