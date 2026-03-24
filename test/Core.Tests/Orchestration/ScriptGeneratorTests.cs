using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class ScriptGeneratorTests
{
    [Fact]
    public void GenerateCsproj_ContainsIAWClientReference()
    {
        var csproj = ScriptGenerator.GenerateCsproj();
        Assert.Contains("Aspire.IAW.Client", csproj);
        Assert.Contains("net11.0", csproj);
        Assert.Contains("Exe", csproj);
    }
}