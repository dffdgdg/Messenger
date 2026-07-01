using Android.Media;
using Core.Services.Media.Abstractions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using IOBinaryWriter = System.IO.BinaryWriter;
using IOMemoryStream = System.IO.MemoryStream;

namespace Mobile.Android.Services.Platform;

public sealed class AndroidAudioRecorderService : IAudioRecorderService, IDisposable
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int BufferMultiplier = 4;

    private AudioRecord? _audioRecord;
    private CancellationTokenSource? _recordCts;
    private Task? _recordTask;

    private readonly Stopwatch _stopwatch = new();
    private readonly Lock _lock = new();
    private readonly Lock _peaksLock = new();

    private IOMemoryStream? _buffer;
    private List<byte> _waveformPeaks = [];
    private bool _disposed;

    public bool IsSupported => AudioRecord.GetMinBufferSize(SampleRate,
        ChannelIn.Mono, Encoding.Pcm16bit) > 0;

    public bool IsRecording => _stopwatch.IsRunning;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (IsRecording) return false;

            CleanupBuffers();
            _buffer = new IOMemoryStream();
            lock (_peaksLock) { _waveformPeaks = []; }
            WriteWavHeader(_buffer, 0);
        }

        try
        {
            int minBufferSize = AudioRecord.GetMinBufferSize(
                SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);

            int bufferSize = Math.Max(minBufferSize * BufferMultiplier,
                SampleRate * Channels * (BitsPerSample / 8) / 10); // ~100ms

            _audioRecord = new AudioRecord(
                AudioSource.Mic,
                SampleRate,
                ChannelIn.Mono,
                Encoding.Pcm16bit,
                bufferSize
            );

            if (_audioRecord.State != State.Initialized)
            {
                _audioRecord.Dispose();
                _audioRecord = null;
                return false;
            }

            _audioRecord.StartRecording();
            _stopwatch.Restart();

            _recordCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _recordTask = Task.Run(() => RecordLoop(_recordCts.Token), ct);

            Debug.WriteLine($"[AndroidRecorder] Recording started, buffer={bufferSize}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidRecorder] Start error: {ex.Message}");
            CleanupBuffers();
            return false;
        }
    }

    public async Task<AudioRecordingResult?> StopAsync(CancellationToken ct = default)
    {
        if (!IsRecording) return null;

        return await StopInternalAsync(cancel: false);
    }

    public async Task CancelAsync()
    {
        if (!IsRecording) return;
        await StopInternalAsync(cancel: true);
    }

    private async Task<AudioRecordingResult?> StopInternalAsync(bool cancel)
    {
        _stopwatch.Stop();
        var duration = _stopwatch.Elapsed;

        // Останавливаем запись
        _recordCts?.Cancel();

        if (_recordTask != null)
        {
            try { await _recordTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
            _recordTask = null;
        }

        try
        {
            _audioRecord?.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AndroidRecorder] Stop error: {ex.Message}");
        }

        if (cancel)
        {
            lock (_lock) { CleanupBuffers(); }
            return null;
        }

        byte[] audioData;
        List<byte> peaks;

        lock (_lock)
        {
            if (_buffer == null) return null;
            audioData = _buffer.ToArray();
            lock (_peaksLock) { peaks = _waveformPeaks; }
            CleanupBuffers();
        }

        FinalizeWavHeader(audioData);

        return new AudioRecordingResult
        {
            AudioStream = new IOMemoryStream(audioData) { Position = 0 },
            FileName = $"voice_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav",
            ContentType = "audio/wav",
            Duration = duration,
            Waveform = DownsampleWaveform(peaks, 100)
        };
    }

    private void RecordLoop(CancellationToken ct)
    {
        var readBuffer = new short[SampleRate * Channels / 10]; // 100ms chunks

        while (!ct.IsCancellationRequested && _audioRecord?.RecordingState == RecordState.Recording)
        {
            try
            {
                int samplesRead = _audioRecord.Read(readBuffer, 0, readBuffer.Length);

                if (samplesRead <= 0)
                {
                    if (samplesRead == (int)TrackStatus.ErrorDeadObject ||
                        samplesRead == (int)TrackStatus.ErrorInvalidOperation)
                    {
                        Debug.WriteLine($"[AndroidRecorder] Read error: {samplesRead}");
                        break;
                    }
                    continue;
                }

                // Копируем данные в буфер
                var bytes = MemoryMarshal.Cast<short, byte>(readBuffer.AsSpan(0, samplesRead));
                lock (_lock) { _buffer?.Write(bytes); }

                // Вычисляем пик для визуализации
                short maxPeak = 0;
                for (int i = 0; i < samplesRead; i++)
                {
                    int abs = readBuffer[i] < 0 ?
                        (readBuffer[i] == short.MinValue ? short.MaxValue : -readBuffer[i]) :
                        readBuffer[i];
                    if (abs > maxPeak) maxPeak = (short)abs;
                }

                lock (_peaksLock) { _waveformPeaks.Add((byte)(maxPeak / 128)); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Debug.WriteLine($"[AndroidRecorder] RecordLoop error: {ex.Message}");
                break;
            }
        }
    }

    private void CleanupBuffers()
    {
        _buffer?.Dispose();
        _buffer = null;
        _stopwatch.Reset();

        _audioRecord?.Release();
        _audioRecord?.Dispose();
        _audioRecord = null;

        _recordCts?.Dispose();
        _recordCts = null;
    }

    private static void WriteWavHeader(System.IO.Stream s, int dataLen)
    {
        const int byteRate = SampleRate * Channels * (BitsPerSample / 8);
        const short blockAlign = Channels * (BitsPerSample / 8);

        using var w = new IOBinaryWriter(s, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)Channels);
        w.Write(SampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write((short)BitsPerSample);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
    }

    private static void FinalizeWavHeader(byte[] data)
    {
        if (data.Length < 44) return;
        var dl = data.Length - 44;
        Buffer.BlockCopy(BitConverter.GetBytes(36 + dl), 0, data, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(dl), 0, data, 40, 4);
    }

    private static string DownsampleWaveform(List<byte> peaks, int bars)
    {
        if (peaks.Count == 0) return string.Empty;
        if (peaks.Count <= bars) return Convert.ToBase64String(peaks.ToArray());

        var result = new byte[bars];
        double step = (double)peaks.Count / bars;
        for (int i = 0; i < bars; i++)
        {
            int start = (int)(i * step);
            int end = Math.Min((int)((i + 1) * step), peaks.Count);
            byte max = 0;
            for (int j = start; j < end; j++)
                if (peaks[j] > max) max = peaks[j];
            result[i] = max;
        }
        return Convert.ToBase64String(result);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _recordCts?.Cancel();
        _recordTask?.GetAwaiter().GetResult();

        lock (_lock) { CleanupBuffers(); }
        _recordCts?.Dispose();
    }
}