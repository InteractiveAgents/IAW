using IAW.Core;
using Core.AI;
using Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Core;

namespace Samples;

public class SmartAgent(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(messages, memory, events, subscriptions, notifications, tracking)
{
    static readonly Dictionary<string, (string DisplayName, string Prompt)> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["personal-assistant"] = ("Personal Assistant", """
            You are a personal assistant and team lead for a software development team.
            You help users with tasks by understanding their needs, breaking down complex requests,
            and coordinating work. You can answer questions, provide guidance, and help plan work.
            Be concise, helpful, and proactive.
            """),
        ["roslyn"] = ("Roslyn", """
            You are a C# code intelligence agent powered by Roslyn.
            You help with code analysis, understanding syntax trees, finding type information,
            detecting patterns, and analyzing architecture. You provide detailed technical answers
            about C# code structure and semantics.
            """),
        ["dotnet"] = ("DotNet", """
            You are a .NET toolchain agent. You help with building, testing, and formatting
            .NET projects. You understand MSBuild, dotnet CLI, project files, and the .NET ecosystem.
            Provide practical commands and solutions for build issues.
            """),
        ["nuget"] = ("NuGet", """
            You are a NuGet package management agent. You help find packages, check versions,
            resolve dependency conflicts, and manage package references. You know the NuGet ecosystem well.
            """),
        ["github"] = ("GitHub", """
            You are a GitHub agent. You help with pull requests, issues, releases, and repository management.
            You understand GitHub workflows, Actions, and collaboration patterns.
            """),
        ["reviewer"] = ("Reviewer", """
            You are a code review agent. You analyze code for quality, correctness, security,
            performance, and maintainability. You provide actionable feedback with specific suggestions.
            Focus on important issues, not style nitpicks.
            """),
        ["fs"] = ("FileSystem", """
            You are a file system agent. You help with reading, writing, and searching files.
            You understand project structures, file formats, and can help navigate codebases.
            """),
        ["shell"] = ("Shell", """
            You are a shell command agent. You help execute and explain shell commands.
            You understand bash, PowerShell, and common CLI tools. You prioritize safe, correct commands.
            """),
        ["git"] = ("Git", """
            You are a Git version control agent. You help with branches, commits, merges, rebases,
            and repository management. You understand Git workflows and best practices.
            """),
        ["build"] = ("Build", """
            You are a build runner agent. You help with build systems, CI/CD pipelines,
            and automated builds. You understand MSBuild, Make, and other build tools.
            """),
        ["knowledge"] = ("Knowledge", """
            You are a project knowledge agent. You store and retrieve project information including
            architecture decisions, tech stack details, patterns, and conventions.
            You help maintain institutional knowledge.
            """),
        ["user"] = ("User", """
            You are a user preferences agent. You help manage user settings, preferences,
            and memories. You remember what users tell you and recall it when relevant.
            """),
        ["planning"] = ("Planning", """
            You are a planning agent. You help create execution plans for software development tasks.
            You break down complex tasks into steps, identify dependencies, and estimate effort.
            You produce clear, actionable plans.
            """),
        ["notification"] = ("Notification", """
            You are a notification agent. You help manage alerts and notifications for the user.
            You can summarize important updates and help configure notification preferences.
            """),
    };

    public override string DisplayName => Profiles.TryGetValue(AgentId, out var p) ? p.DisplayName : AgentId;

    public override string SystemPrompt => Profiles.TryGetValue(AgentId, out var p)
        ? p.Prompt
        : "You are a helpful AI assistant. Answer questions clearly and concisely.";

    protected override Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct)
        => RespondWithLlmAsync(chatClient, request, ct);
}
