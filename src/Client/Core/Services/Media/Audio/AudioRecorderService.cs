using Core.Services.Media.Abstractions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using IOBinaryWriter = System.IO.BinaryWriter;
using IOMemoryStream = System.IO.MemoryStream;

namespace Core.Services.Media.Audio;

public sealed class AudioRecorderService : IAudioRecorderService, IAsyncDisposable, IDisposable
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private readonly IAudioCaptureDevice _capture;
    private readonly Stopwatch _stopwatch = new();
    private readonly Lock _lock = new();

    private readonly Lock _peaksLock = new();
    private List<byte> _waveformPeaks = [];

    private IOMemoryStream? _buffer;
    private bool _disposed;

    public bool IsSupported => _capture.IsAvailable;
    public bool IsRecording => _stopwatch.IsRunning;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public AudioRecorderService(IAudioCaptureDeviceFactory captureFactory)
    {
        _capture = captureFactory.Create();
        _capture.SamplesAvailable += OnSamplesAvailable;
    }

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
            await _capture.StartAsync(SampleRate, ct);
            _stopwatch.Restart();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioRecorder] Start error: {ex.Message}");
            CleanupBuffers();
            return false;
        }
    }

    public async Task<AudioRecordingResult?> StopAsync(CancellationToken ct = default)
    {
        if (!IsRecording) return null;

        _stopwatch.Stop();
        var duration = _stopwatch.Elapsed;

        await _capture.StopAsync();

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

    public async Task CancelAsync()
    {
        _stopwatch.Stop();
        await _capture.StopAsync();
        lock (_lock) { CleanupBuffers(); }
    }

    private void OnSamplesAvailable(object? sender, short[] samples)
    {
        var bytes = MemoryMarshal.Cast<short, byte>(samples.AsSpan());
        lock (_lock) { _buffer?.Write(bytes); }

        short maxPeak = 0;
        foreach (var s in samples)
        {
            int abs = s < 0 ? (s == short.MinValue ? short.MaxValue : -s) : s;
            if (abs > maxPeak) maxPeak = (short)abs;
        }

        lock (_peaksLock) { _waveformPeaks.Add((byte)(maxPeak / 128)); }
    }

    private void CleanupBuffers()
    {
        _buffer?.Dispose();
        _buffer = null;
        _stopwatch.Reset();
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
        w.Write(16); w.Write((short)1);
        w.Write((short)Channels); w.Write(SampleRate);
        w.Write(byteRate); w.Write(blockAlign);
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _capture.SamplesAvailable -= OnSamplesAvailable;
        await CancelAsync();
        lock (_lock) { CleanupBuffers(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capture.SamplesAvailable -= OnSamplesAvailable;
        _capture.StopAsync().GetAwaiter().GetResult();
        lock (_lock) { CleanupBuffers(); }
    }
}