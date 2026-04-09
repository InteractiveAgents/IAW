using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class ScriptGeneratorTests
{
    [Fact]
    public void GenerateCsproj_ContainsIAWClientReference()
    {
        var csproj = ScriptGenerator.GenerateCsproj();
        Assert.True(csproj.Contains("Aspire.Client") || csproj.Contains("Aspire.IAW.Client"),
            "Should reference client project (local path or NuGet package)");
        Assert.Contains("net11.0", csproj);
        Assert.Contains("Exe", csproj);
    }
}