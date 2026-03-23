namespace Core.AI.Models;

public sealed class WhisperSmall : WhisperModel
{
    public override string Id => "whisper-small";
    public override string DisplayName => "Whisper Small";
    public override int Priority => 50;
}