using Concentus;
using Concentus.Enums;

namespace API.Application.Services.Features.Call;

public sealed class ParticipantCodec
{
    public const int SampleRate = 48000;
    public const int Channels = 1;
    public const int FrameSamples = 960;
    public const int MaxEncodedBytes = 4000;

    private readonly IOpusEncoder _encoder;
    private readonly IOpusDecoder _decoder;
    private readonly Lock _encoderLock = new();
    private readonly Lock _decoderLock = new();

    public ParticipantCodec()
    {
        _encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 32000;
        _encoder.Complexity = 2;
        _encoder.UseVBR = true;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        _decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
    }

    public bool TryDecode(ReadOnlySpan<byte> opusData, float[] pcm)
    {
        if (pcm.Length < FrameSamples || opusData.IsEmpty) return false;
        lock (_decoderLock)
        {
            var samples = _decoder.Decode(opusData, pcm.AsSpan(0, FrameSamples), FrameSamples);
            return samples > 0;
        }
    }

    /// <summary>
    /// Единственный метод encode — принимает Span для совместимости
    /// как с полевыми буферами (float[]), так и с арендованными из пула.
    /// </summary>
    public int Encode(ReadOnlySpan<float> pcm, Span<byte> opusBuffer)
    {
        if (pcm.Length < FrameSamples || opusBuffer.IsEmpty) return 0;
        lock (_encoderLock)
        {
            return _encoder.Encode(
                pcm[..FrameSamples],
                FrameSamples,
                opusBuffer,
                opusBuffer.Length);
        }
    }
}