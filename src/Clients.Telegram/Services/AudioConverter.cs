using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace TelegramClient.Services;

public interface IAudioConverter
{
    Task<string> ConvertOggToWavAsync(Stream oggStream, CancellationToken ct = default);
}

public sealed class AudioConverter : IAudioConverter
{
    const int SampleRate = 48000;
    const int Channels = 1;

    public async Task<string> ConvertOggToWavAsync(Stream oggStream, CancellationToken ct = default)
    {
        var wavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

        var decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
        var allSamples = new List<short>();

        var oggIn = new OpusOggReadStream(decoder, oggStream);
        while (oggIn.HasNextPacket)
        {
            ct.ThrowIfCancellationRequested();
            var samples = oggIn.DecodeNextPacket();
            if (samples is not null)
                allSamples.AddRange(samples);
        }

        await using var wavWriter = new WaveFileWriter(wavPath,
            new WaveFormat(SampleRate, 16, Channels));
        var byteBuffer = new byte[allSamples.Count * 2];
        Buffer.BlockCopy(allSamples.ToArray(), 0, byteBuffer, 0, byteBuffer.Length);
        await wavWriter.WriteAsync(byteBuffer, ct);

        return wavPath;
    }
}
