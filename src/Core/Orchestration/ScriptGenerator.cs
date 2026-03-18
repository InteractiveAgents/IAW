namespace Core.Orchestration;

public static class ScriptGenerator
{
    public static string GenerateCsproj()
    {
        var clientProjectPath = FindClientProject();
        var agentsProjectPath = clientProjectPath.Replace(
            Path.Combine("Aspire.IAW.Client", "Aspire.IAW.Client.csproj"),
            Path.Combine("Agents", "Agents.csproj"));
        var agentsCSharpPath = clientProjectPath.Replace(
            Path.Combine("Aspire.IAW.Client", "Aspire.IAW.Client.csproj"),
            Path.Combine("Agents.CSharp", "Agents.CSharp.csproj"));

        var refs = $"""<ProjectReference Include="{clientProjectPath}" />""";
        if (File.Exists(agentsProjectPath))
            refs += $"\n    <ProjectReference Include=\"{agentsProjectPath}\" />";
        if (File.Exists(agentsCSharpPath))
            refs += $"\n    <ProjectReference Include=\"{agentsCSharpPath}\" />";

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net11.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                {refs}
              </ItemGroup>
            </Project>
            """;
    }

    static string FindClientProject()
    {
        // Walk up from the workspace to find the IAW repo root
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(current, "src", "Aspire.IAW.Client", "Aspire.IAW.Client.csproj");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        // Fallback: check IAW__Workspace env var's parent directories
        var workspace = Environment.GetEnvironmentVariable("IAW__Workspace");
        if (!string.IsNullOrEmpty(workspace))
        {
            current = workspace;
            for (var i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(current, "src", "Aspire.IAW.Client", "Aspire.IAW.Client.csproj");
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(current);
                if (parent is null) break;
                current = parent.FullName;
            }
        }

        // Last resort: search common locations
        var known = new[]
        {
            @"E:\IAW\src\Aspire.IAW.Client\Aspire.IAW.Client.csproj",
            @"C:\IAW\src\Aspire.IAW.Client\Aspire.IAW.Client.csproj",
        };
        foreach (var path in known)
            if (File.Exists(path)) return path;

        // Absolute fallback — hope the NuGet package exists
        return "Aspire.IAW.Client";
    }
}
