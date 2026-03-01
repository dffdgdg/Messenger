using MessengerDesktop.Services.Audio;
using System;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Инкапсулирует состояние записи голоса: таймер, состояние, elapsed.
/// </summary>
public sealed partial class VoiceRecordingViewModel(IAudioRecorderService recorder) : ObservableObject, IDisposable
{
    private DispatcherTimer? _timer;
    private bool _disposed;

    [ObservableProperty] private AudioRecordingState _state = AudioRecordingState.Idle;
    [ObservableProperty] private TimeSpan _elapsed;
    [ObservableProperty] private string _elapsedFormatted = "0:00";
    [ObservableProperty] private string? _errorMessage;

    public bool IsIdle => State == AudioRecordingState.Idle;
    public bool IsRecording => State == AudioRecordingState.Recording;
    public bool IsSending => State == AudioRecordingState.Sending;
    public bool HasError => State == AudioRecordingState.Error;

    public void StartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => UpdateElapsed();
        _timer.Start();
    }

    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void UpdateElapsed()
    {
        Elapsed = recorder.Elapsed;
        ElapsedFormatted = Elapsed.ToString(@"m\:ss");
    }

    partial void OnStateChanged(AudioRecordingState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsSending));
        OnPropertyChanged(nameof(HasError));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTimer();
        GC.SuppressFinalize(this);
    }
}