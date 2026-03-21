using Core.Contracts.UI;

namespace IAW.Agents.Orchestration;

public interface IThreadUI : IGrainWithStringKey
{
    Task<PendingOptions?> ConsumePendingOptions(CancellationToken ct);
}
