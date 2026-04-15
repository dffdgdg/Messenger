using PortAudioSharp;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IOStream = System.IO.Stream;
using IOMemoryStream = System.IO.MemoryStream;
using IOBinaryWriter = System.IO.BinaryWriter;

namespace MessengerDesktop.Services.Audio;

public sealed class AudioRecorderService : IAudioRecorderService, IDisposable
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int FramesPerBuffer = 512;

    private PortAudioSharp.Stream? _paStream;
    private IOMemoryStream? _buffer;
    private readonly Stopwatch _stopwatch = new();
    private readonly Lock _lock = new();
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
                WriteWavHeader(_buffer, 0);

                if (PortAudio.DefaultInputDevice == PortAudio.NoDevice)
                {
                    Debug.WriteLine("[AudioRecorder] No input device found");
                    return Task.FromResult(false);
                }

                var inputParams = new StreamParameters
                {
                    device = PortAudio.DefaultInputDevice,
                    channelCount = Channels,
                    sampleFormat = SampleFormat.Int16,
                    suggestedLatency = PortAudio
                        .GetDeviceInfo(PortAudio.DefaultInputDevice)
                        .defaultLowInputLatency,
                    hostApiSpecificStreamInfo = IntPtr.Zero
                };

                var capturedBuffer = _buffer;

                _paStream = new Stream(inParams: inputParams, outParams: null, sampleRate: SampleRate, framesPerBuffer: FramesPerBuffer, streamFlags: StreamFlags.ClipOff, callback: (input, _, frameCount, ref _, _, _) =>
                {
                    OnAudioData(input, (long)frameCount, capturedBuffer);
                    return StreamCallbackResult.Continue;
                },
                userData: IntPtr.Zero);

                _paStream.Start();
                _stopwatch.Restart();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioRecorder] Start failed: {ex.Message}");
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
                    Debug.WriteLine($"[AudioRecorder] Stream stop warning: {ex.Message}");
                }

                var audioData = _buffer.ToArray();
                FinalizeWavHeader(audioData);

                var resultStream = new IOMemoryStream(audioData) { Position = 0 };

                var result = new AudioRecordingResult
                {
                    AudioStream = resultStream,
                    FileName = $"voice_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav",
                    ContentType = "audio/wav",
                    Duration = duration
                };

                CleanupInternal();
                return Task.FromResult<AudioRecordingResult?>(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioRecorder] Stop failed: {ex.Message}");
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
                    Debug.WriteLine($"[AudioRecorder] Cancel warning: {ex.Message}");
                }
            }

            CleanupInternal();
        }

        return Task.CompletedTask;
    }

    private static void OnAudioData(IntPtr input, long frameCount, IOMemoryStream buffer)
    {
        if (input == IntPtr.Zero) return;

        var byteCount = (int)(frameCount * Channels * (BitsPerSample / 8));

        var temp = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(input, temp, 0, byteCount);
            lock (buffer)
            {
                buffer.Write(temp, 0, byteCount);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioRecorder] OnAudioData error: {ex.Message}");
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(temp);
        }
    }

    private static void WriteWavHeader(IOStream stream, int dataLength)
    {
        const int byteRate = SampleRate * Channels * (BitsPerSample / 8);
        const short blockAlign = (short)(Channels * (BitsPerSample / 8));

        using var writer = new IOBinaryWriter(
            stream,
            System.Text.Encoding.ASCII,
            leaveOpen: true);

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
                Debug.WriteLine($"[AudioRecorder] Dispose warning: {ex.Message}");
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
            Debug.WriteLine($"[AudioRecorder] CheckSupported failed: {ex.Message}");
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
                Debug.WriteLine($"[AudioRecorder] Terminate warning: {ex.Message}");
            }
        }
    }
}