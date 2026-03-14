using Concentus;
using Concentus.Oggfile;
using Core.AI;
using NAudio.Wave;

namespace TelegramClient.Services;

public sealed class AudioConverter : IAudioConverter
{
    const int SampleRate = 48000;
    const int BitsPerSample = 16;
    const int Channels = 1;
    const int BytesPerSample = 2;

    public string ConvertToWav(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var outputPath = Path.Combine(Path.GetTempPath(), $"iaw_converted_{Guid.NewGuid()}.wav");

        using var fileIn = File.OpenRead(inputPath);
        using var decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
        var oggIn = new OpusOggReadStream(decoder, fileIn);
        using var wavWriter = new WaveFileWriter(outputPath, new WaveFormat(SampleRate, BitsPerSample, Channels));

        while (oggIn.HasNextPacket)
        {
            var samples = oggIn.DecodeNextPacket();
            if (samples is not null && samples.Length > 0)
            {
                var bytes = new byte[samples.Length * BytesPerSample];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                wavWriter.Write(bytes, 0, bytes.Length);
            }
        }

        return outputPath;
    }
}
