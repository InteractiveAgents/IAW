using Microsoft.AspNetCore.SignalR;

namespace DevUI.Visualization;

public class AgentVisualizationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
