using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Core.AI;

public interface ILlmProviderFactory
{
    string ProviderName { get; }
    bool IsConfigured(IConfiguration config);
    IChatClient CreateClient(LLMModel model, IConfiguration config);
}
