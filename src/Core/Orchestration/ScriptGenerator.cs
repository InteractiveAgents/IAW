using System.Text;

namespace Core.Orchestration;

public static class ScriptGenerator
{
    public static string Generate(OrchestrationPlan plan, string clusterEndpoint, int gatewayPort)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using Orleans;");
        sb.AppendLine("using Orleans.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using System.Net;");
        sb.AppendLine("using IAW.Core;");
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

            var grainId = $"orchestrated-{step.AgentType.ToLowerInvariant()}";
            sb.AppendLine($"var agent{step.Order} = client.GetGrain<IAgent>(\"{grainId}\");");

            if (step.Parameters.TryGetValue("workspace", out var workspace))
            {
                sb.AppendLine($"await agent{step.Order}.SetWorkspaceAsync(\"{EscapeString(workspace)}\");");
            }

            if (step.Parameters.TryGetValue("message", out var message))
            {
                sb.AppendLine($"await foreach (var response in agent{step.Order}.SendMessageAsync(");
                sb.AppendLine($"    new ChatMessage(\"{EscapeString(message)}\")))");
                sb.AppendLine("{");
                sb.AppendLine("    if (response.Kind == AgentResponseKind.Text)");
                sb.AppendLine("        Console.Write(response.Content);");
                sb.AppendLine("}");
                sb.AppendLine("Console.WriteLine();");
            }

            sb.AppendLine();
        }

        sb.AppendLine("await host.StopAsync();");
        sb.AppendLine("Console.WriteLine(\"Orchestration complete.\");");

        return sb.ToString();
    }

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
