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

        var riff = new string(reader.ReadChars(4));
        if (riff != "RIFF")
            throw new InvalidDataException("Not a RIFF file");

        reader.ReadInt32();

        var wave = new string(reader.ReadChars(4));
        if (wave != "WAVE")
            throw new InvalidDataException("Not a WAVE file");

        int sampleRate = 0, channels = 0, bitsPerSample = 0;
        short[]? samples = null;

        while (stream.Position < stream.Length - 8)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();

            switch (chunkId)
            {
                case "fmt ":
                    var audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();

                    var remaining = chunkSize - 16;
                    if (remaining > 0)
                        reader.ReadBytes(remaining);

                    if (audioFormat != 1)
                        throw new NotSupportedException($"Only PCM WAV supported, got format {audioFormat}");
                    if (bitsPerSample != 16)
                        throw new NotSupportedException($"Only 16-bit WAV supported, got {bitsPerSample} bits");
                    break;

                case "data":
                    var rawBytes = reader.ReadBytes(chunkSize);
                    samples = new short[rawBytes.Length / 2];
                    Buffer.BlockCopy(rawBytes, 0, samples, 0, rawBytes.Length);
                    break;

                default:
                    reader.ReadBytes(chunkSize);
                    break;
            }

            if (samples != null && sampleRate != 0) break;
        }

        if (samples == null || sampleRate == 0 || channels == 0)
            throw new InvalidDataException("WAV file is missing fmt or data chunk");

        return new WavData(samples, sampleRate, channels);
    }
}