using System;
using System.IO;

namespace MessengerDesktop.Services.Audio;

public interface IAudioPlayerService : IDisposable
{
    bool IsPlaying { get; }
    bool IsPaused { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int? CurrentMessageId { get; }

    event Action<int>? PlaybackStarted;
    event Action<int>? PlaybackPaused;
    event Action<int>? PlaybackResumed;
    event Action<int>? PlaybackStopped;
    event Action<int, TimeSpan>? PositionChanged;

    void Play(int messageId, Stream audioStream);
    void Pause();
    void Resume();
    void Stop();
    void Seek(double positionPercent);
}