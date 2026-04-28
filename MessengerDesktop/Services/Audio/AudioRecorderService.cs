using PortAudioSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IOBinaryWriter = System.IO.BinaryWriter;
using IOMemoryStream = System.IO.MemoryStream;
using IOStream = System.IO.Stream;

namespace MessengerDesktop.Services.Audio;

public sealed class AudioRecorderService : IAudioRecorderService, IDisposable
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int FramesPerBuffer = 512;

    private Stream? _paStream;
    private IOMemoryStream? _buffer;
    private readonly Stopwatch _stopwatch = new();
    private readonly Lock _lock = new();
    private readonly object _bufferLock = new();
    private List<byte> _waveformPeaks = [];
    private bool _disposed;
    private bool _portAudioInitialized;

    public bool IsSupported => CheckSupported();
    public bool IsRecording => _paStream != null && _stopwatch.IsRunning;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public Task<bool> StartAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (IsRecording)
                return Task.FromResult(false);

            try
            {
                EnsurePortAudioInitialized();
                CleanupInternal();

                _buffer = new IOMemoryStream();
                _waveformPeaks = [];
                WriteWavHeader(_buffer, 0);

                if (PortAudio.DefaultInputDevice == PortAudio.NoDevice)
                {
                    Debug.WriteLine("[AudioRecorder] микрофон не обнаружен");
                    return Task.FromResult(false);
                }

                var inParams = new StreamParameters
                {
                    device = PortAudio.DefaultInputDevice,
                    channelCount = Channels,
                    sampleFormat = SampleFormat.Int16,
                    suggestedLatency = PortAudio.GetDeviceInfo(PortAudio.DefaultInputDevice).defaultLowInputLatency,
                    hostApiSpecificStreamInfo = IntPtr.Zero
                };

                var capturedBuffer = _buffer;
                var capturedLock = _bufferLock;
                var capturedPeaks = _waveformPeaks;

                _paStream = new Stream(inParams, outParams: null, SampleRate, FramesPerBuffer, StreamFlags.ClipOff,
                    callback: (input, _, frameCount, ref _, _, _) =>
                    {
                        OnAudioData(input, frameCount, capturedBuffer, capturedLock, capturedPeaks);
                        return StreamCallbackResult.Continue;
                    },
                    userData: IntPtr.Zero);

                _paStream.Start();
                _stopwatch.Restart();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioRecorder] ошибка запуска: {ex.Message}");
                CleanupInternal();
                return Task.FromResult(false);
            }
        }
    }

    public Task<AudioRecordingResult?> StopAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!IsRecording || _paStream == null || _buffer == null)
                return Task.FromResult<AudioRecordingResult?>(null);

            try
            {
                _stopwatch.Stop();
                var duration = _stopwatch.Elapsed;

                try { _paStream.Stop(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioRecorder] предупреждение остановки потока: {ex.Message}");
                }

                var audioData = _buffer.ToArray();
                FinalizeWavHeader(audioData);

                var resultStream = new IOMemoryStream(audioData) { Position = 0 };

                var result = new AudioRecordingResult
                {
                    AudioStream = resultStream,
                    FileName = $"voice_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav",
                    ContentType = "audio/wav",
                    Duration = duration,
                    Waveform = DownsampleWaveform(_waveformPeaks, 100)
                };

                CleanupInternal();
                return Task.FromResult<AudioRecordingResult?>(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioRecorder] ошибка остановки: {ex.Message}");
                CleanupInternal();
                return Task.FromResult<AudioRecordingResult?>(null);
            }
        }
    }

    public Task CancelAsync()
    {
        lock (_lock)
        {
            if (_paStream != null)
            {
                _stopwatch.Stop();
                try { _paStream.Stop(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioRecorder] предупреждение отмены: {ex.Message}");
                }
            }

            CleanupInternal();
        }

        return Task.CompletedTask;
    }

    private static void OnAudioData(IntPtr input, long frameCount, IOMemoryStream buffer, object bufferLock, List<byte> peaks)
    {
        if (input == IntPtr.Zero) return;

        var byteCount = (int)(frameCount * Channels * (BitsPerSample / 8));
        var temp = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            Marshal.Copy(input, temp, 0, byteCount);

            lock (bufferLock)
            {
                buffer.Write(temp, 0, byteCount);
            }

            short maxPeak = 0;
            int sampleCount = byteCount / 2;
            var span = new ReadOnlySpan<byte>(temp, 0, byteCount);

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(span.Slice(i * 2, 2));
                if (sample > maxPeak) maxPeak = sample;
                else if (-sample > maxPeak) maxPeak = (short)-sample;
            }

            lock (peaks)
            {
                peaks.Add((byte)(maxPeak / 128));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioRecorder] OnAudioData ошибка: {ex.Message}");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
        }
    }

    private static string DownsampleWaveform(List<byte> peaks, int targetBars)
    {
        if (peaks.Count == 0) return string.Empty;
        if (peaks.Count <= targetBars) return Convert.ToBase64String(peaks.ToArray());

        var result = new byte[targetBars];
        double step = (double)peaks.Count / targetBars;

        for (int i = 0; i < targetBars; i++)
        {
            int start = (int)(i * step);
            int end = (int)((i + 1) * step);
            end = Math.Min(end, peaks.Count);

            byte max = 0;
            for (int j = start; j < end; j++)
            {
                if (peaks[j] > max) max = peaks[j];
            }
            result[i] = max;
        }

        return Convert.ToBase64String(result);
    }

    private static void WriteWavHeader(IOStream stream, int dataLength)
    {
        const int byteRate = SampleRate * Channels * (BitsPerSample / 8);
        const short blockAlign = Channels * (BitsPerSample / 8);

        using var writer = new IOBinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)Channels);
        writer.Write(SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)BitsPerSample);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
    }

    private static void FinalizeWavHeader(byte[] data)
    {
        var dataLength = data.Length - 44;
        if (dataLength < 0) return;

        var chunkSize = BitConverter.GetBytes(36 + dataLength);
        Buffer.BlockCopy(chunkSize, 0, data, 4, 4);

        var subchunkSize = BitConverter.GetBytes(dataLength);
        Buffer.BlockCopy(subchunkSize, 0, data, 40, 4);
    }

    private void EnsurePortAudioInitialized()
    {
        if (_portAudioInitialized) return;
        PortAudio.Initialize();
        _portAudioInitialized = true;
    }

    private void CleanupInternal()
    {
        if (_paStream != null)
        {
            try { _paStream.Dispose(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioRecorder] Dispose предупреждение: {ex.Message}");
            }
            _paStream = null;
        }

        _buffer?.Dispose();
        _buffer = null;
        _stopwatch.Reset();
    }

    private bool CheckSupported()
    {
        try
        {
            EnsurePortAudioInitialized();
            return PortAudio.DefaultInputDevice != PortAudio.NoDevice;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioRecorder] CheckSupported ошибка: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock) { CleanupInternal(); }

        if (_portAudioInitialized)
        {
            try { PortAudio.Terminate(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioRecorder] Terminate предупреждение: {ex.Message}");
            }
        }
    }
}