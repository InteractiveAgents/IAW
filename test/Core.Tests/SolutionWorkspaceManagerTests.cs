using IAW.Agents.CSharp.Roslyn.Workspace;
using Xunit;

namespace IAW.Core.Tests;

public class SolutionWorkspaceManagerTests
{
    [Fact]
    public void IsReady_BeforeLoad_ReturnsFalse()
    {
        var manager = new SolutionWorkspaceManager();
        Assert.False(manager.IsReady);
    }

    [Fact]
    public void GetCompilation_BeforeLoad_ReturnsNull()
    {
        var manager = new SolutionWorkspaceManager();
        Assert.Null(manager.GetCompilation("SomeProject"));
    }

    [Fact]
    public void GetProjectNames_BeforeLoad_ReturnsEmpty()
    {
        var manager = new SolutionWorkspaceManager();
        Assert.Empty(manager.GetProjectNames());
    }

    [Fact]
    public void Dispose_BeforeLoad_DoesNotThrow()
    {
        var manager = new SolutionWorkspaceManager();
        manager.Dispose();
    }
}
