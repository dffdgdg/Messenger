using Shared.Enum;

namespace Core.Services.Features.Call;

/// <summary>
/// Заглушка для Android — звонки через WebRTC без нативного аудио пока не реализованы.
/// </summary>
public sealed class AndroidCallAudioService : ICallAudioService
{
    public bool IsRunning => false;
    public bool IsMuted { get; private set; }
    public bool NoiseSuppressionEnabled { get; set; }

    public event Action<byte[], int>? OnEncodedFrame;
    public event Action<bool>? SpeakingStateChanged;

    public void Start() { }
    public void Stop() { }
    public void SetMuted(bool muted) => IsMuted = muted;
    public void SetMode(CallMode mode) { }
    public void AddParticipant(int userId) { }
    public void RemoveParticipant(int userId) { }
    public bool HasParticipant(int userId) => false;
    public void ReceiveEncodedAudio(int fromUserId, byte[] opusData, int length) { }
    public void ReceiveMixedAudio(byte[] opusData) { }
    public void Dispose() { }
}