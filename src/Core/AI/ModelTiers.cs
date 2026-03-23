namespace Core.AI;

public sealed class Fast : LLMModel
{
    internal Fast() : base("tier-fast", "tier", "Fast") { }
}

public sealed class Balanced : LLMModel
{
    internal Balanced() : base("tier-balanced", "tier", "Balanced") { }
}

public sealed class Reasoning : LLMModel
{
    internal Reasoning() : base("tier-reasoning", "tier", "Reasoning") { }
}
