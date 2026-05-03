using System;
using System.Numerics;

namespace Desktop.Services.Features.Call;

/// <summary>
/// <para>
/// Лёгкое шумоподавление без overlap-add.
/// Основано на спектральном подавлении с Wiener-подобным gain,
/// сглаживанием по времени и частоте.
/// </para>
/// <para>
/// Важно:
/// - не потокобезопасен
/// - рассчитан на fixed-size фреймы
/// </para>
/// </summary>
public sealed class NoiseReducer
{
    private const float Eps = 1e-12f;

    // Почти цифровая тишина: можно просто занулить.
    private const float HardSilenceThreshold = 0.0008f;

    // Мягкий gate для очень тихих кадров.
    private const float SoftGateBegin = 0.0025f;
    private const float SoftGateEnd = 0.0070f;

    // Простая эвристика VAD для решения:
    // можно ли считать кадр речью и как обновлять шумовую модель.
    private const float VadRmsThreshold = 0.0090f;
    private const float SpeechSnrThreshold = 1.45f;
    private const float SpeechActiveBinsThreshold = 0.10f;

    // Decision-directed коэффициент.
    // Чем выше, тем мягче/стабильнее подавление, но больше "хвостов".
    private const float DecisionDirected = 0.96f;

    // Минимально допустимый gain.
    // Слишком низкие значения обычно создают musical noise.
    private const float MinGainSpeech = 0.12f;
    private const float MinGainNoise = 0.06f;

    // Сколько первых кадров использовать для грубой инициализации шумовой модели.
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

    /// <summary>
    /// Обрабатывает фрейм PCM16 in-place.
    /// Возвращает true, если кадр похож на речь.
    /// </summary>
    public bool Process(Span<short> frame)
    {
        if (frame.Length != _frameSize)
            throw new ArgumentException($"Ожидался фрейм длиной {_frameSize}, получено {frame.Length}.", nameof(frame));

        if (!IsEnabled)
            return true;

        // PCM16 -> float [-1..1]
        float rms = 0f;
        for (int i = 0; i < _frameSize; i++)
        {
            float s = frame[i] / 32768f;
            _timeBuf[i] = s;
            rms += s * s;
        }

        rms = MathF.Sqrt(rms / _frameSize);

        // Если это уже почти цифровая тишина, смысла обрабатывать нет.
        if (_initialized && rms < HardSilenceThreshold)
        {
            frame.Clear();
            return false;
        }

        // Без окна: у нас нет overlap-add.
        for (int i = 0; i < _frameSize; i++)
            _fftBuf[i] = new Complex(_timeBuf[i], 0.0);

        for (int i = _frameSize; i < _fftSize; i++)
            _fftBuf[i] = Complex.Zero;

        Fft(_fftBuf, forward: true);

        float sumPower = 0f;
        float sumNoise = 0f;
        int activeBins = 0;

        for (int k = 0; k < _bins; k++)
        {
            double re = _fftBuf[k].Real;
            double im = _fftBuf[k].Imaginary;
            float p = (float)((re * re) + (im * im));

            if (p < Eps) p = Eps;
            _power[k] = p;

            sumPower += p;

            float n = _initialized ? MathF.Max(_noisePower[k], Eps) : p;
            sumNoise += n;

            if (_initialized && k > 2 && k < _bins - 3 && p > (3f * n))
                activeBins++;
        }

        bool speechLikely = DetectSpeech(rms, sumPower, sumNoise, activeBins);

        UpdateNoiseEstimate(speechLikely);

        // Пока шумовая модель не набралась — не подавляем спектр,
        // только чуть приглушаем совсем тихие кадры.
        if (!_initialized)
        {
            float gate = ComputeGateGain(rms);
            for (int i = 0; i < _frameSize; i++)
                frame[i] = FloatToPcm16(_timeBuf[i] * gate);

            return speechLikely;
        }

        float frameMinGain = speechLikely ? MinGainSpeech : MinGainNoise;

        // 1) Базовый gain по каждому бину
        for (int k = 0; k < _bins; k++)
        {
            float noise = MathF.Max(_noisePower[k], Eps);
            float postSnr = _power[k] / noise;

            float prioriSnr =
                (DecisionDirected * _prevGain[k] * _prevGain[k] * _prevPostSnr[k]) +
                ((1f - DecisionDirected) * MathF.Max(postSnr - 1f, 0f));

            float gain = prioriSnr / (1f + prioriSnr);
            gain = Math.Clamp(gain, frameMinGain, 1f);

            _rawGain[k] = gain;
            _prevPostSnr[k] = postSnr;
        }

        // 2) Сглаживание по частоте
        if (_bins == 1)
        {
            _freqGain[0] = _rawGain[0];
        }
        else
        {
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

        float outputEnergy = 0f;

        // 3) Сглаживание по времени + применение gain
        for (int k = 0; k < _bins; k++)
        {
            float target = _freqGain[k];
            float prev = _prevGain[k];

            // Быстрее уходим в подавление, медленнее возвращаемся обратно.
            float alpha = target < prev ? 0.35f : 0.85f;
            float gain = (alpha * prev) + ((1f - alpha) * target);

            // В неречевых кадрах давим чуть сильнее.
            if (!speechLikely)
                gain = MathF.Min(gain, (0.85f * target) + 0.05f);

            gain = Math.Clamp(gain, frameMinGain, 1f);
            _prevGain[k] = gain;

            _fftBuf[k] *= gain;

            if (k > 0 && k < _bins - 1)
                _fftBuf[_fftSize - k] = Complex.Conjugate(_fftBuf[k]);

            outputEnergy += _power[k] * gain * gain;
        }

        // Для real-signal IFFT эти бины должны быть real.
        _fftBuf[0] = new Complex(_fftBuf[0].Real, 0.0);
        if ((_fftSize & 1) == 0)
            _fftBuf[_fftSize / 2] = new Complex(_fftBuf[_fftSize / 2].Real, 0.0);

        Fft(_fftBuf, forward: false);

        float gateGain = ComputeGateGain(rms);

        for (int i = 0; i < _frameSize; i++)
        {
            float v = (float)(_fftBuf[i].Real / _fftSize);
            v *= gateGain;

            frame[i] = FloatToPcm16(v);
        }

        return speechLikely || (outputEnergy / _bins) > 1e-7f;
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
        // Стартовая инициализация: мягкое приближение к нижней огибающей.
        if (!_initialized)
        {
            if (_startupFrames == 0)
            {
                Array.Copy(_power, _noisePower, _bins);
            }
            else
            {
                for (int k = 0; k < _bins; k++)
                {
                    float p = _power[k];
                    float candidate = MathF.Min(_noisePower[k], p);
                    _noisePower[k] = (0.85f * _noisePower[k]) + (0.15f * candidate);

                    if (_noisePower[k] < Eps)
                        _noisePower[k] = Eps;
                }
            }

            _startupFrames++;
            if (_startupFrames >= StartupNoiseFrames)
                _initialized = true;

            return;
        }

        // Основное обновление:
        // - на неречевых кадрах обновляем заметно быстрее
        // - на речевых — очень осторожно, чтобы не "выучить" голос как шум
        for (int k = 0; k < _bins; k++)
        {
            float p = _power[k];
            float n = _noisePower[k];

            float alpha;

            if (!speechLikely || p <= (1.5f * n))
            {
                // Бин похож на шум
                alpha = p > n ? 0.08f : 0.04f;
            }
            else
            {
                // Похоже на речь: обновляем шум очень медленно
                alpha = p > n ? 0.002f : 0.01f;
            }

            _noisePower[k] = ((1f - alpha) * n) + (alpha * p);

            if (_noisePower[k] < Eps)
                _noisePower[k] = Eps;
        }
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

        // smoothstep
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

    /// <summary>Итеративный in-place FFT Cooley-Tukey. Размер — степень 2.</summary>
    private static void Fft(Complex[] buf, bool forward)
    {
        int n = buf.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;

            j ^= bit;

            if (i < j)
                (buf[i], buf[j]) = (buf[j], buf[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2.0 * Math.PI / len * (forward ? -1.0 : 1.0);
            Complex wLen = new(Math.Cos(ang), Math.Sin(ang));

            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                int half = len >> 1;

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
}