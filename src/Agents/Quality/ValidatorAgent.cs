using Core.AI;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Quality;

public class ValidatorAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient)
    : Agent<IValidator>(durableState, chatClient), IValidator
{
    protected override int MaxHistoryMessages => 10;

    public async Task<ValidationReport> ValidateTaskAsync(
        string taskId, string originalRequest, CancellationToken ct = default)
    {
        var ledger = GrainFactory.GetGrain<ITaskLedger>(taskId);
        var events = await ledger.GetEventsAsync(ct);

        if (events.Count == 0)
            return new ValidationReport(taskId, false,
                new List<ValidationIssue> { new("critical", "No events found in task ledger — task may not have started") },
                "No activity recorded");

        var contextBlock = await ledger.GetContextBlockAsync(ct: ct);

        var prompt = $"""
            Validate this task execution against the original request.

            Original request: {originalRequest}

            Task activity log:
            {contextBlock}

            Check for:
            1. Does the activity address the original request?
            2. Are there any inconsistencies between steps?
            3. Did any agent report failures that weren't resolved?
            4. Is there evidence the task completed successfully?

            Respond in this exact format:
            PASSED: true/false
            ISSUES:
            - [severity] description (evidence: ...)
            SUMMARY: one-line summary
            """;

        var response = await ChatClient.GetResponseAsync(
            [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, prompt)], cancellationToken: ct);

        return ParseValidationResponse(taskId, response.Text ?? "");
    }

    public async Task<ValidationReport> ValidateConsistencyAsync(
        string taskId, Dictionary<string, string> expectedValues, CancellationToken ct = default)
    {
        var ledger = GrainFactory.GetGrain<ITaskLedger>(taskId);
        var events = await ledger.GetEventsAsync(ct);
        var issues = new List<ValidationIssue>();

        foreach (var (key, expected) in expectedValues)
        {
            var found = events.Any(e =>
                e.Result.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
                (e.Detail?.Contains(expected, StringComparison.OrdinalIgnoreCase) ?? false));

            if (!found)
                issues.Add(new ValidationIssue("warning",
                    $"Expected value '{expected}' for '{key}' not found in any task event",
                    Evidence: $"Searched {events.Count} events"));
        }

        var passed = issues.Count == 0;
        return new ValidationReport(taskId, passed, issues,
            passed ? "All expected values found" : $"{issues.Count} consistency issues detected");
    }

    private static ValidationReport ParseValidationResponse(string taskId, string response)
    {
        var passed = response.Contains("PASSED: true", StringComparison.OrdinalIgnoreCase);
        var issues = new List<ValidationIssue>();

        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ["))
            {
                var endBracket = trimmed.IndexOf(']');
                if (endBracket > 3)
                {
                    var severity = trimmed[3..endBracket];
                    var description = trimmed[(endBracket + 2)..].Trim();
                    issues.Add(new ValidationIssue(severity, description));
                }
            }
        }

        var summaryLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("SUMMARY:"));
        var summary = summaryLine?.Replace("SUMMARY:", "").Trim() ?? "Validation complete";

        return new ValidationReport(taskId, passed, issues, summary);
    }
}
