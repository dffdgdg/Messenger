using System.Numerics;

namespace Core.Services.Call;

public sealed class NoiseReducer
{
    private const float Eps = 1e-12f;

    private const float HardSilenceThreshold = 0.0008f;

    private const float SoftGateBegin = 0.0025f;
    private const float SoftGateEnd = 0.0070f;

    private const float VadRmsThreshold = 0.0090f;
    private const float SpeechSnrThreshold = 1.45f;
    private const float SpeechActiveBinsThreshold = 0.10f;

    private const float DecisionDirected = 0.96f;

    private const float MinGainSpeech = 0.12f;
    private const float MinGainNoise = 0.06f;

    private const int StartupNoiseFrames = 12;

    private readonly int _frameSize;
    private readonly int _fftSize;
    private readonly int _bins;

    private readonly float[] _timeBuf;
    private readonly Complex[] _fftBuf;

    private readonly float[] _power;
    private readonly float[] _noisePower;

    private readonly float[] _rawGain;
    private readonly float[] _freqGain;
    private readonly float[] _prevGain;
    private readonly float[] _prevPostSnr;

    private int _startupFrames;
    private bool _initialized;

    public bool IsEnabled { get; set; } = true;

    public NoiseReducer(int frameSize = 960)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameSize);

        _frameSize = frameSize;
        _fftSize = NextPow2(frameSize);
        _bins = (_fftSize / 2) + 1;

        _timeBuf = new float[_frameSize];
        _fftBuf = new Complex[_fftSize];

        _power = new float[_bins];
        _noisePower = new float[_bins];

        _rawGain = new float[_bins];
        _freqGain = new float[_bins];
        _prevGain = new float[_bins];
        _prevPostSnr = new float[_bins];

        Reset();
    }

    public void Reset()
    {
        Array.Clear(_timeBuf, 0, _timeBuf.Length);
        Array.Clear(_fftBuf, 0, _fftBuf.Length);
        Array.Clear(_power, 0, _power.Length);
        Array.Clear(_noisePower, 0, _noisePower.Length);
        Array.Clear(_rawGain, 0, _rawGain.Length);
        Array.Clear(_freqGain, 0, _freqGain.Length);
        Array.Clear(_prevPostSnr, 0, _prevPostSnr.Length);

        for (int i = 0; i < _prevGain.Length; i++)
            _prevGain[i] = 1f;

        _startupFrames = 0;
        _initialized = false;
    }

    public bool Process(Span<short> frame)
    {
        if (frame.Length != _frameSize)
            throw new ArgumentException($"Ожидался фрейм длиной {_frameSize}, получено {frame.Length}.", nameof(frame));

        if (!IsEnabled)
            return true;

        float rms = ConvertToFloat(frame);

        if (_initialized && rms < HardSilenceThreshold)
        {
            frame.Clear();
            return false;
        }

        LoadFftBuffer();

        Fft(_fftBuf, forward: true);

        var (sumPower, sumNoise, activeBins) = ComputeSpectralStats();

        bool speechLikely = DetectSpeech(rms, sumPower, sumNoise, activeBins);

        UpdateNoiseEstimate(speechLikely);

        if (!_initialized)
        {
            ApplyGateOnly(frame, rms);
            return speechLikely;
        }

        float frameMinGain = speechLikely ? MinGainSpeech : MinGainNoise;

        ComputeRawGain(frameMinGain);
        SmoothGainInFrequency();
        float outputEnergy = ApplyGainAndReconstruct(speechLikely, frameMinGain);

        RestoreRealSignalSymmetry();
        Fft(_fftBuf, forward: false);

        WriteOutputFrame(frame, rms);

        return speechLikely || (outputEnergy / _bins) > 1e-7f;
    }

    private float ConvertToFloat(Span<short> frame)
    {
        float rms = 0f;

        for (int i = 0; i < _frameSize; i++)
        {
            float s = frame[i] / 32768f;
            _timeBuf[i] = s;
            rms += s * s;
        }

        return MathF.Sqrt(rms / _frameSize);
    }

    private void LoadFftBuffer()
    {
        for (int i = 0; i < _frameSize; i++)
            _fftBuf[i] = new Complex(_timeBuf[i], 0.0);

        for (int i = _frameSize; i < _fftSize; i++)
            _fftBuf[i] = Complex.Zero;
    }

    private (float SumPower, float SumNoise, int ActiveBins) ComputeSpectralStats()
    {
        float sumPower = 0f;
        float sumNoise = 0f;
        int activeBins = 0;

        for (int k = 0; k < _bins; k++)
        {
            float p = ComputeBinPower(k);
            _power[k] = p;
            sumPower += p;

            float n = _initialized ? MathF.Max(_noisePower[k], Eps) : p;
            sumNoise += n;

            if (IsBinActive(k, p, n))
                activeBins++;
        }

        return (sumPower, sumNoise, activeBins);
    }

    private float ComputeBinPower(int k)
    {
        double re = _fftBuf[k].Real;
        double im = _fftBuf[k].Imaginary;
        float p = (float)((re * re) + (im * im));
        return p < Eps ? Eps : p;
    }

    private bool IsBinActive(int k, float power, float noise)
        => _initialized && k > 2 && k < _bins - 3 && power > (3f * noise);

    private void ApplyGateOnly(Span<short> frame, float rms)
    {
        float gate = ComputeGateGain(rms);

        for (int i = 0; i < _frameSize; i++)
            frame[i] = FloatToPcm16(_timeBuf[i] * gate);
    }

    private void ComputeRawGain(float minGain)
    {
        for (int k = 0; k < _bins; k++)
        {
            float noise = MathF.Max(_noisePower[k], Eps);
            float postSnr = _power[k] / noise;

            float prioriSnr =
                (DecisionDirected * _prevGain[k] * _prevGain[k] * _prevPostSnr[k]) +
                ((1f - DecisionDirected) * MathF.Max(postSnr - 1f, 0f));

            float gain = prioriSnr / (1f + prioriSnr);
            gain = Math.Clamp(gain, minGain, 1f);

            _rawGain[k] = gain;
            _prevPostSnr[k] = postSnr;
        }
    }

    private void SmoothGainInFrequency()
    {
        if (_bins == 1)
        {
            _freqGain[0] = _rawGain[0];
            return;
        }

        _freqGain[0] = (0.75f * _rawGain[0]) + (0.25f * _rawGain[1]);

        for (int k = 1; k < _bins - 1; k++)
        {
            _freqGain[k] =
                (0.25f * _rawGain[k - 1]) +
                (0.50f * _rawGain[k]) +
                (0.25f * _rawGain[k + 1]);
        }

        _freqGain[_bins - 1] =
            (0.25f * _rawGain[_bins - 2]) +
            (0.75f * _rawGain[_bins - 1]);
    }

    private float ApplyGainAndReconstruct(bool speechLikely, float minGain)
    {
        float outputEnergy = 0f;

        for (int k = 0; k < _bins; k++)
        {
            float gain = ComputeSmoothedGain(k, speechLikely, minGain);

            _prevGain[k] = gain;
            _fftBuf[k] *= gain;

            if (k > 0 && k < _bins - 1)
                _fftBuf[_fftSize - k] = Complex.Conjugate(_fftBuf[k]);

            outputEnergy += _power[k] * gain * gain;
        }

        return outputEnergy;
    }

    private float ComputeSmoothedGain(int k, bool speechLikely, float minGain)
    {
        float target = _freqGain[k];
        float prev = _prevGain[k];

        float alpha = target < prev ? 0.35f : 0.85f;
        float gain = (alpha * prev) + ((1f - alpha) * target);

        if (!speechLikely)
            gain = MathF.Min(gain, (0.85f * target) + 0.05f);

        return Math.Clamp(gain, minGain, 1f);
    }

    private void RestoreRealSignalSymmetry()
    {
        _fftBuf[0] = new Complex(_fftBuf[0].Real, 0.0);

        if ((_fftSize & 1) == 0)
            _fftBuf[_fftSize / 2] = new Complex(_fftBuf[_fftSize / 2].Real, 0.0);
    }

    private void WriteOutputFrame(Span<short> frame, float rms)
    {
        float gateGain = ComputeGateGain(rms);
        float normFactor = 1f / _fftSize;

        for (int i = 0; i < _frameSize; i++)
        {
            float v = (float)(_fftBuf[i].Real * normFactor) * gateGain;
            frame[i] = FloatToPcm16(v);
        }
    }

    private bool DetectSpeech(float rms, float sumPower, float sumNoise, int activeBins)
    {
        if (!_initialized)
            return rms >= VadRmsThreshold;

        float frameSnr = sumPower / MathF.Max(sumNoise, Eps);
        float activeRatio = activeBins / (float)Math.Max(_bins - 6, 1);

        return rms >= VadRmsThreshold &&
               (frameSnr >= SpeechSnrThreshold || activeRatio >= SpeechActiveBinsThreshold);
    }

    private void UpdateNoiseEstimate(bool speechLikely)
    {
        if (!_initialized)
        {
            UpdateNoiseStartup();
            return;
        }

        UpdateNoiseTracking(speechLikely);
    }

    private void UpdateNoiseStartup()
    {
        if (_startupFrames == 0)
        {
            Array.Copy(_power, _noisePower, _bins);
        }
        else
        {
            for (int k = 0; k < _bins; k++)
            {
                float candidate = MathF.Min(_noisePower[k], _power[k]);
                _noisePower[k] = MathF.Max((0.85f * _noisePower[k]) + (0.15f * candidate), Eps);
            }
        }

        _startupFrames++;

        if (_startupFrames >= StartupNoiseFrames)
            _initialized = true;
    }

    private void UpdateNoiseTracking(bool speechLikely)
    {
        for (int k = 0; k < _bins; k++)
        {
            float p = _power[k];
            float n = _noisePower[k];
            float alpha = SelectNoiseAlpha(speechLikely, p, n);
            _noisePower[k] = MathF.Max(((1f - alpha) * n) + (alpha * p), Eps);
        }
    }

    private static float SelectNoiseAlpha(bool speechLikely, float power, float noise)
    {
        if (!speechLikely || power <= (1.5f * noise))
        {
            return power > noise ? 0.08f : 0.04f;
        }

        return power > noise ? 0.002f : 0.01f;
    }

    private static float ComputeGateGain(float rms)
    {
        if (rms <= HardSilenceThreshold)
            return 0f;

        if (rms <= SoftGateBegin)
            return 0.10f;

        if (rms >= SoftGateEnd)
            return 1f;

        float t = (rms - SoftGateBegin) / (SoftGateEnd - SoftGateBegin);
        t = Math.Clamp(t, 0f, 1f);

        t = t * t * (3f - (2f * t));

        return 0.10f + (0.90f * t);
    }

    private static short FloatToPcm16(float v)
    {
        v = Math.Clamp(v, -1f, 0.9999695f);
        int s = (int)MathF.Round(v * 32767f);
        s = Math.Clamp(s, -32768, 32767);
        return (short)s;
    }

    private static int NextPow2(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    private static void Fft(Complex[] buf, bool forward)
    {
        int n = buf.Length;

        BitReversePermutation(buf, n);

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2.0 * Math.PI / len * (forward ? -1.0 : 1.0);
            Complex wLen = new(Math.Cos(ang), Math.Sin(ang));

            ApplyButterfly(buf, n, len, wLen);
        }
    }

    private static void BitReversePermutation(Complex[] buf, int n)
    {
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;

            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;

            j ^= bit;

            if (i < j)
                (buf[i], buf[j]) = (buf[j], buf[i]);
        }
    }

    private static void ApplyButterfly(Complex[] buf, int n, int len, Complex wLen)
    {
        int half = len >> 1;

        for (int i = 0; i < n; i += len)
        {
            Complex w = Complex.One;

            for (int j = 0; j < half; j++)
            {
                Complex u = buf[i + j];
                Complex v = buf[i + j + half] * w;

                buf[i + j] = u + v;
                buf[i + j + half] = u - v;

                w *= wLen;
            }
        }
    }
}