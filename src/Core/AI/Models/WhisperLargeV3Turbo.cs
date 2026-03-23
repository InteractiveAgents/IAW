namespace Core.AI.Models;

public sealed class WhisperLargeV3Turbo : WhisperModel
{
    public override string Id => "whisper-large-v3-turbo";
    public override string DisplayName => "Whisper Large V3 Turbo";
    public override int Priority => 100;
}