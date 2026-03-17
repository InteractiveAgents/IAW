using Orleans.Runtime;

namespace Core.Contracts;

public interface ICodeOrchestrator : IAgent
{
    [ResponseTimeout("00:15:00")]
    Task<string> ExecuteCodeOrchestration(string plan, CancellationToken ct = default);
}
