using Core.Deployment;
using Xunit;

namespace IAW.Core.Tests.Scheduling;

public class DeployVerifyJobTests
{
    [Fact]
    public void VerifyBuildOutput_Success_ReturnsHealthy()
    {
        var result = DeployVerifier.VerifyBuildOutput("Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        Assert.True(result.Success);
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public void VerifyBuildOutput_Failure_ReturnsUnhealthy()
    {
        var result = DeployVerifier.VerifyBuildOutput("Build FAILED.\n    3 Error(s)");
        Assert.False(result.Success);
        Assert.Equal(3, result.Errors);
    }

    [Fact]
    public void VerifyBuildOutput_NullOrEmpty_ReturnsUnhealthy()
    {
        var result = DeployVerifier.VerifyBuildOutput("");
        Assert.False(result.Success);
    }

    [Fact]
    public void ShouldRevert_BuildFailed_ReturnsTrue()
    {
        var result = new BuildVerification(false, 3, "Build FAILED");
        Assert.True(DeployVerifier.ShouldRevert(result));
    }

    [Fact]
    public void ShouldRevert_BuildSucceeded_ReturnsFalse()
    {
        var result = new BuildVerification(true, 0, "Build succeeded");
        Assert.False(DeployVerifier.ShouldRevert(result));
    }
}
