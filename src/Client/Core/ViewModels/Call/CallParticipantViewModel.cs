using CommunityToolkit.Mvvm.ComponentModel;
using Shared.Dto.Call;

namespace Core.ViewModels.Call;

public partial class CallParticipantViewModel(CallParticipantDto dto) : ObservableObject
{
    public int UserId { get; } = dto.UserId;
    public string DisplayName { get; } = dto.DisplayName;
    public string? AvatarUrl { get; } = dto.AvatarUrl;
    [ObservableProperty] public partial bool IsMuted { get; set; } = dto.IsMuted;
    [ObservableProperty] public partial double AvatarSize { get; set; } = 56;
    [ObservableProperty] public partial bool IsSpeaking { get; set; } = dto.IsSpeaking;

    private bool _hasVideo;

    public bool HasVideo
    {
        get => _hasVideo;
        set
        {
            if (_hasVideo == value) return;
            _hasVideo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowAvatar));
        }
    }

    public bool ShowAvatar => !HasVideo;
}