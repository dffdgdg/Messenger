namespace Core.Services.Call.Abstractions;

public interface ICallAudioService : IDisposable
{
    bool IsRunning { get; }
    bool IsMuted { get; }
    bool NoiseSuppressionEnabled { get; set; }

    event Action<byte[], int>? OnEncodedFrame;
    event Action<bool>? SpeakingStateChanged;

    void Start();
    void Stop();
    void SetMuted(bool muted);
    void SetMode(CallMode mode);
    void AddParticipant(int userId);
    void RemoveParticipant(int userId);
    bool HasParticipant(int userId);
    void ReceiveEncodedAudio(int fromUserId, byte[] opusData, int length);
    void ReceiveMixedAudio(byte[] opusData);
}