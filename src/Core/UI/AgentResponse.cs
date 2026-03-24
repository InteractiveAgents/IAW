namespace Core.UI;

[GenerateSerializer]
public record AgentResponse(
    [property: Id(0)] List<UIPart> Parts);