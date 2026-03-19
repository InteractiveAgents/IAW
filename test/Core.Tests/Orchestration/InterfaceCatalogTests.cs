using Core.Orchestration;
using IAW.Agents.Coding;
using IAW.Agents.System;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class InterfaceCatalogTests
{
    [Theory]
    [InlineData(typeof(IRoslyn), "roslyn")]
    [InlineData(typeof(IFileSystem), "file-system")]
    [InlineData(typeof(IDotNet), "dot-net")]
    [InlineData(typeof(INuGet), "nu-get")]
    [InlineData(typeof(IGit), "git")]
    [InlineData(typeof(IShell), "shell")]
    public void ComputeGrainId_converts_interface_to_kebab_case(Type interfaceType, string expected)
    {
        var grainId = InterfaceCatalog.ComputeGrainId(interfaceType);
        Assert.Equal(expected, grainId);
    }

    [Fact]
    public void Discover_finds_all_agent_interfaces()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.Contains(catalog, e => e.InterfaceName == "IRoslyn");
        Assert.Contains(catalog, e => e.InterfaceName == "IFileSystem");
        Assert.Contains(catalog, e => e.InterfaceName == "IDotNet");
    }

    [Fact]
    public void Discover_excludes_base_interfaces()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.DoesNotContain(catalog, e => e.InterfaceName == "IAgent");
        Assert.DoesNotContain(catalog, e => e.InterfaceName == "IDynamicAgent");
    }

    [Fact]
    public void Discover_entries_have_correct_grain_ids()
    {
        var catalog = InterfaceCatalog.Discover();

        var roslyn = catalog.Single(e => e.InterfaceName == "IRoslyn");
        Assert.Equal("roslyn", roslyn.GrainId);

        var fileSystem = catalog.Single(e => e.InterfaceName == "IFileSystem");
        Assert.Equal("file-system", fileSystem.GrainId);

    }

    [Fact]
    public void Discover_detects_receiver_on_IDotNet()
    {
        var catalog = InterfaceCatalog.Discover();
        var dotNet = catalog.Single(e => e.InterfaceName == "IDotNet");
        // IDotNet implements IReceiver<CodeChangedMessage>
        Assert.Contains("CodeChangedMessage", dotNet.Receives);
    }

    [Fact]
    public void ToPromptString_generates_LLM_readable_catalog()
    {
        var catalog = InterfaceCatalog.Discover();
        var prompt = InterfaceCatalog.ToPromptString(catalog);
        Assert.Contains("IRoslyn", prompt);
        Assert.Contains("IFileSystem", prompt);
        Assert.Contains("roslyn", prompt);
        Assert.Contains("file-system", prompt);
    }

    [Fact]
    public void ToPromptString_includes_receiver_info()
    {
        var catalog = InterfaceCatalog.Discover();
        var prompt = InterfaceCatalog.ToPromptString(catalog);
        Assert.Contains("CodeChangedMessage", prompt);
    }

    [Fact]
    public void Discover_returns_interface_type_reference()
    {
        var catalog = InterfaceCatalog.Discover();
        var roslyn = catalog.Single(e => e.InterfaceName == "IRoslyn");
        Assert.Equal(typeof(IRoslyn), roslyn.InterfaceType);
    }
}
