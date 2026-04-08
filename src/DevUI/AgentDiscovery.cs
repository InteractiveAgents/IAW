using Core.Contracts;
using Microsoft.Agents.AI.Hosting;
using System.Text.RegularExpressions;

namespace DevUI;

static partial class AgentDiscovery
{
    // Maps grain interface type to its well-known grain ID.
    // Convention: strip "I" prefix, convert PascalCase to kebab-case.
    // e.g. IPersonalAssistant → personal-assistant, IRoslyn → roslyn
    public static List<IHostedAgentBuilder> DiscoverAndRegisterAgents(IHostApplicationBuilder builder)
    {
        var agentInterfaces = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t.IsInterface
                         && t != typeof(IAgent)
                         && typeof(IAgent).IsAssignableFrom(t)
                         && !t.IsGenericType)
            .ToList();

        var agentRefs = new List<IHostedAgentBuilder>(agentInterfaces.Count);

        foreach (var iface in agentInterfaces)
        {
            var name = iface.Name;
            if (name.StartsWith('I') && name.Length > 1 && char.IsUpper(name[1]))
                name = name[1..];

            var grainId = ToKebabCase(name);
            var displayName = ToSpacedName(name);

            // First line = grain ID for routing; kebab-case name is URL-safe for per-agent endpoints
            var instructions = $"{grainId}\n{displayName} — Interactive Agent in the IAW system.";
            var description = $"{displayName} — Interactive Agent in the IAW system.";

            var agentRef = builder.AddAIAgent(grainId, instructions, description, chatClientServiceKey: null);
            agentRefs.Add(agentRef);
        }

        return agentRefs;
    }

    private static string ToKebabCase(string pascalCase)
    {
        var kebab = KebabRegex().Replace(pascalCase, "-$1").ToLowerInvariant();
        return kebab.TrimStart('-');
    }

    private static string ToSpacedName(string pascalCase)
    {
        return KebabRegex().Replace(pascalCase, " $1").Trim();
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex KebabRegex();
}
