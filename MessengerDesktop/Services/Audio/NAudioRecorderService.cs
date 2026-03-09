using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Services.Audio;

public sealed class NAudioRecorderService : IAudioRecorderService, IDisposable
{
    private static readonly WaveFormat RecordingFormat = new(16000, 16, 1);

    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsSupported => CheckSupported();
    public bool IsRecording => _waveIn != null && _stopwatch.IsRunning;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public Task<bool> StartAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (IsRecording)
                return Task.FromResult(false);

            try
            {
                CleanupInternal();

                _buffer = new MemoryStream();
                _waveIn = new WaveInEvent
                {
                    WaveFormat = RecordingFormat,
                    BufferMilliseconds = 100
                };
                _writer = new WaveFileWriter(new IgnoreDisposeStream(_buffer), RecordingFormat);

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _waveIn.StartRecording();
                _stopwatch.Restart();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NAudioRecorder] Start failed: {ex.Message}");
                CleanupInternal();
                return Task.FromResult(false);
            }
        }
    }

    public Task<AudioRecordingResult?> StopAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!IsRecording || _waveIn == null || _writer == null || _buffer == null)
                return Task.FromResult<AudioRecordingResult?>(null);

            try
            {
                _stopwatch.Stop();
                _waveIn.StopRecording();

                _writer.Flush();
                _writer.Dispose();
                _writer = null;

                var duration = _stopwatch.Elapsed;

                _buffer.Position = 0;
                var resultStream = new MemoryStream(
                    _buffer.ToArray())
                { Position = 0 };

                var result = new AudioRecordingResult
                {
                    AudioStream = resultStream,
                    FileName =
                        $"voice_{DateTime.UtcNow:yyyyMMdd_HHmmss}.wav",
                    ContentType = "audio/wav",
                    Duration = duration
                };

                CleanupInternal();
                return Task.FromResult<AudioRecordingResult?>(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[NAudioRecorder] Stop failed: {ex.Message}");
                CleanupInternal();
                return Task.FromResult<AudioRecordingResult?>(null);
            }
        }
    }

    public Task CancelAsync()
    {
        lock (_lock)
        {
            if (IsRecording)
            {
                _stopwatch.Stop();
                _waveIn?.StopRecording();
            }
            CleanupInternal();
        }
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            try
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            }
            catch (ObjectDisposedException)
            {
                // Writer disposed between check and write — OK
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Debug.WriteLine($"[NAudioRecorder] Recording error: {e.Exception.Message}");
        }
    }

    private void CleanupInternal()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _writer?.Dispose();
        _writer = null;

        _buffer?.Dispose();
        _buffer = null;

        _stopwatch.Reset();
    }

    private static bool CheckSupported()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return WaveInEvent.DeviceCount > 0;

            return OperatingSystem.IsLinux();
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock) { CleanupInternal(); }
    }

    /// <summary>
    /// Обёртка, которая не закрывает underlying stream при Dispose.
    /// Нужна, чтобы WaveFileWriter.Dispose() не закрыл MemoryStream.
    /// </summary>
    private sealed class IgnoreDisposeStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin)
            => inner.Seek(offset, origin);
        public override void SetLength(long value)
            => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count)
            => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            // Intentionally empty — не закрываем inner
        }
    }
}