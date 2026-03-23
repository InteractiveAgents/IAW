using Core.Tools;
using Xunit;

namespace IAW.Core.Tests;

public class WorkspaceToolsTests
{
    [Fact]
    public void SetWorkspace_AbsolutePath_Succeeds()
    {
        string? current = null;
        var tools = new WorkspaceTools(() => current!, path => current = path);

        var result = tools.SetWorkspace("/some/absolute/path");

        Assert.Contains("Workspace set to", result);
        Assert.Equal("/some/absolute/path", current);
    }

    [Fact]
    public void SetWorkspace_RelativePath_ReturnsError()
    {
        string? current = null;
        var tools = new WorkspaceTools(() => current!, path => current = path);

        var result = tools.SetWorkspace("relative/path");

        Assert.Contains("Error", result);
        Assert.Null(current);
    }

    [Fact]
    public void GetWorkspace_ReturnsCurrentPath()
    {
        var tools = new WorkspaceTools(() => "/workspace/path", _ => { });

        var result = tools.GetWorkspace();

        Assert.Equal("/workspace/path", result);
    }
}