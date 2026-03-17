namespace Core.Orchestration;

public static class ScriptGenerator
{
    public static string GenerateCsproj() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net11.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Aspire.IAW.Client" Version="*" />
          </ItemGroup>
        </Project>
        """;
}
