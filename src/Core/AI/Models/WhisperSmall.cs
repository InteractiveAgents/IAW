namespace Core.AI.Models;

public sealed class WhisperSmall : WhisperModel
{
    public static readonly WhisperSmall Instance = new();
    private WhisperSmall() { }
    public override string Id => "whisper-small";
    public override string DisplayName => "Whisper Small";
    public override int Priority => 50;
}
