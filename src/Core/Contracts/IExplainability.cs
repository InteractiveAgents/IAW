namespace Core.Contracts;

public interface IExplainability : IAgent
{
    static string IAgent.AgentDisplayName => "Explainability";
    static string IAgent.AgentDescription => "Traces decisions back to conversations, preferences, and memories";
    static string[] IAgent.AgentCapabilities => ["trace-decision", "search-memories", "explain-choice"];
    static string IAgent.AgentInstructions => """
        You are the Explainability Agent. When a user asks "why did you do X?" or
        "why was this decision made?", you search across all memory layers and construct
        a traced explanation with specific dates, conversation references, and decision records.

        Always cite your sources: which memory type, when it was stored, and what it says.
        Be specific — "On March 15, you said latency matters more than cost" is better than
        "You previously mentioned preferring low latency."
        """;

    Task<ExplanationResult> ExplainAsync(string question, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryTrace>> SearchAllMemoriesAsync(string query, int topK = 5, CancellationToken ct = default);
}

[GenerateSerializer]
public record ExplanationResult(
    [property: Id(0)] string Question,
    [property: Id(1)] string Explanation,
    [property: Id(2)] IReadOnlyList<MemoryTrace> Sources,
    [property: Id(3)] DateTimeOffset Timestamp = default)
{
    public ExplanationResult(string question, string explanation, IReadOnlyList<MemoryTrace> sources)
        : this(question, explanation, sources, DateTimeOffset.UtcNow) { }
}

[GenerateSerializer]
public record MemoryTrace(
    [property: Id(0)] string MemoryType,
    [property: Id(1)] string Content,
    [property: Id(2)] string Source);
