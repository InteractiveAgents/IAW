namespace IAW.Core.AI;

public sealed record ModelCapabilities(
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsStreaming,
    bool SupportsStructuredOutput)
{
    public static readonly ModelCapabilities FullyCapable = new(true, true, true, true);
    public static readonly ModelCapabilities ChatOnly = new(false, false, true, false);
    public static readonly ModelCapabilities ToolCapable = new(true, false, true, true);
}
