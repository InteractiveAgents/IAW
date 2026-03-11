using System.Text;

namespace Core.Orchestration;

public static class ScriptGenerator
{
    public static string Generate(OrchestrationPlan plan, string clusterEndpoint, int gatewayPort, string? workspace = null)
    {
        var catalog = InterfaceCatalog.Discover();
        var sb = new StringBuilder();

        sb.AppendLine("using Orleans;");
        sb.AppendLine("using Orleans.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using System.Net;");
        sb.AppendLine("using Core.Contracts;");

        var namespaces = new HashSet<string>();
        foreach (var step in plan.Steps)
        {
            var entry = FindCatalogEntry(catalog, step.AgentType);
            if (entry is not null && entry.InterfaceType.Namespace is not null)
                namespaces.Add(entry.InterfaceType.Namespace);
        }
        foreach (var ns in namespaces.OrderBy(n => n))
            sb.AppendLine($"using {ns};");
        sb.AppendLine();

        sb.AppendLine($"// Plan: {plan.Summary}");
        sb.AppendLine($"// Steps: {plan.Steps.Count}");
        sb.AppendLine();

        sb.AppendLine("var builder = Host.CreateApplicationBuilder(args);");
        sb.AppendLine("builder.UseOrleansClient(client =>");
        sb.AppendLine("{");
        sb.AppendLine("    client.UseStaticClustering(options =>");
        sb.AppendLine($"        options.Gateways.Add(new IPEndPoint(IPAddress.Parse(\"{clusterEndpoint}\"), {gatewayPort}).ToGatewayUri()));");
        sb.AppendLine("});");
        sb.AppendLine();
        sb.AppendLine("using var host = builder.Build();");
        sb.AppendLine("await host.StartAsync();");
        sb.AppendLine("var client = host.Services.GetRequiredService<IClusterClient>();");
        sb.AppendLine("Console.WriteLine(\"Connected to cluster.\");");
        sb.AppendLine();

        foreach (var step in plan.Steps.OrderBy(s => s.Order))
        {
            sb.AppendLine($"// Step {step.Order}: {step.Action} via {step.AgentType}");
            sb.AppendLine($"Console.WriteLine(\"Step {step.Order}: {step.Action}\");");

            var entry = FindCatalogEntry(catalog, step.AgentType);
            var interfaceName = entry?.InterfaceName ?? "IAgent";
            var grainId = entry?.GrainId ?? step.AgentType.ToLowerInvariant();

            sb.AppendLine($"var agent{step.Order} = client.GetGrain<{interfaceName}>(\"{grainId}\");");

            if (step.Parameters.TryGetValue("workspace", out var ws))
                sb.AppendLine($"await agent{step.Order}.SetWorkspace(\"{EscapeString(ws)}\", default);");
            else if (workspace is not null)
                sb.AppendLine($"await agent{step.Order}.SetWorkspace(\"{EscapeString(workspace)}\", default);");

            if (step.Parameters.TryGetValue("message", out var message))
            {
                sb.AppendLine($"var response{step.Order} = await agent{step.Order}.GetResponse(\"{EscapeString(message)}\", default);");
                sb.AppendLine($"Console.WriteLine(response{step.Order});");
            }

            sb.AppendLine();
        }

        sb.AppendLine("await host.StopAsync();");
        sb.AppendLine("Console.WriteLine(\"Orchestration complete.\");");

        return sb.ToString();
    }

    private static InterfaceCatalog.CatalogEntry? FindCatalogEntry(
        IReadOnlyList<InterfaceCatalog.CatalogEntry> catalog, string agentType)
        => catalog.FirstOrDefault(e =>
            e.GrainId.Equals(agentType, StringComparison.OrdinalIgnoreCase) ||
            e.InterfaceName.Equals($"I{agentType}", StringComparison.OrdinalIgnoreCase));

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
