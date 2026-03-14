namespace Core.AI.Models;

public sealed class WhisperTiny : WhisperModel
{
    public static readonly WhisperTiny Instance = new();
    private WhisperTiny() { }
    public override string Id => "whisper-tiny";
    public override string DisplayName => "Whisper Tiny";
    public override int Priority => 10;
}
