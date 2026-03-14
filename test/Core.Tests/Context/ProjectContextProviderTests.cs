using Core.Context;
using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests.Context;

public class ProjectContextProviderTests
{
    [Fact]
    public void Has_correct_name()
    {
        var provider = new ProjectContextProvider([], new Dictionary<string, FileReference>());
        Assert.Equal("project-context", provider.Name);
    }

    [Fact]
    public void Implements_IAgentContextProvider()
    {
        Assert.True(typeof(IAgentContextProvider).IsAssignableFrom(typeof(ProjectContextProvider)));
    }

    [Fact]
    public async Task Returns_empty_on_error()
    {
        var provider = new ProjectContextProvider(null!, null!);
        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Returns_project_summary_with_counts()
    {
        var tasks = new List<ProjectTask>
        {
            new() { Id = "t1", Description = "Task 1", Status = ProjectTaskStatus.Pending }
        };
        var files = new Dictionary<string, FileReference>
        {
            ["readme.md"] = new("blob://readme", "readme.md", "text/markdown", 1024, false, DateTimeOffset.UtcNow)
        };
        var provider = new ProjectContextProvider(tasks, files);

        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);

        Assert.Contains(result, s => s.Contains("[project]") && s.Contains("1 tasks") && s.Contains("1 files"));
    }

    [Fact]
    public async Task Returns_file_list_when_files_exist()
    {
        var files = new Dictionary<string, FileReference>
        {
            ["report.pdf"] = new("blob://report", "report.pdf", "application/pdf", 2048, true, DateTimeOffset.UtcNow),
            ["notes.txt"] = new("blob://notes", "notes.txt", "text/plain", 512, false, DateTimeOffset.UtcNow)
        };
        var provider = new ProjectContextProvider(new List<ProjectTask>(), files);

        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);

        Assert.Contains(result, s => s.StartsWith("[project files]") && s.Contains("report.pdf") && s.Contains("notes.txt"));
    }

    [Fact]
    public async Task Returns_no_files_line_when_empty()
    {
        var provider = new ProjectContextProvider(new List<ProjectTask>(), new Dictionary<string, FileReference>());

        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(result, s => s.StartsWith("[project files]"));
    }
}
