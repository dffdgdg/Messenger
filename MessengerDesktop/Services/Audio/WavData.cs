using System;
using System.IO;
using System.Text;

namespace MessengerDesktop.Services.Audio;

internal sealed class WavData
{
    public short[] Samples { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public long TotalSamples { get; }
    public TimeSpan Duration { get; }

    private WavData(short[] samples, int sampleRate, int channels)
    {
        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
        TotalSamples = samples.Length / channels;
        Duration = TimeSpan.FromSeconds((double)TotalSamples / sampleRate);
    }

    public static WavData Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        ReadRiffHeader(reader);

        var (sampleRate, channels) = ReadFmtChunk(reader);
        var samples = ReadDataChunk(reader);

        return new WavData(samples, sampleRate, channels);
    }

    private static void ReadRiffHeader(BinaryReader reader)
    {
        if (ReadTag(reader) != "RIFF")
            throw new InvalidDataException("Not a RIFF file");

        reader.ReadInt32();

        if (ReadTag(reader) != "WAVE")
            throw new InvalidDataException("Not a WAVE file");
    }

    private static (int sampleRate, int channels) ReadFmtChunk(BinaryReader reader)
    {
        if (ReadTag(reader) != "fmt ")
            throw new InvalidDataException("Expected 'fmt ' chunk");

        var chunkSize = reader.ReadInt32();
        var audioFormat = reader.ReadInt16();
        var channels = reader.ReadInt16();
        var sampleRate = reader.ReadInt32();

        reader.ReadInt32(); // byteRate
        reader.ReadInt16(); // blockAlign

        var bitsPerSample = reader.ReadInt16();

        if (chunkSize > 16)
            reader.ReadBytes(chunkSize - 16);

        if (audioFormat != 1)
            throw new NotSupportedException($"Поддерживается только PCM WAV, получен формат {audioFormat}");

        if (bitsPerSample != 16)
            throw new NotSupportedException($"Поддерживается только 16-битный WAV, получено {bitsPerSample} бит");

        return (sampleRate, channels);
    }

    private static short[] ReadDataChunk(BinaryReader reader)
    {
        while (ReadTag(reader) != "data")
        {
            var chunkSize = reader.ReadInt32();
            reader.ReadBytes(chunkSize);
        }

        var dataSize = reader.ReadInt32();
        var rawBytes = reader.ReadBytes(dataSize);

        var samples = new short[rawBytes.Length / 2];
        Buffer.BlockCopy(rawBytes, 0, samples, 0, rawBytes.Length);
        return samples;
    }

    private static string ReadTag(BinaryReader reader) =>
        new(reader.ReadChars(4));
}