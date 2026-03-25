using System;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class UserListItemViewModel(UserDto user, bool isSelected = false) : ObservableObject
{
    public UserDto User { get; } = user ?? throw new ArgumentNullException(nameof(user));

    [ObservableProperty] public partial bool IsSelected { get; set; } = isSelected;

    public int Id => User.Id;
    public string DisplayName => User.DisplayName ?? User.Username ?? "Пользователь";
    public string Username => $"@{User.Username}";
    public string? AvatarUrl => User.Avatar;
    public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);

    public UserListItemViewModel Clone(bool? isSelected = null) => new(User, isSelected ?? IsSelected);
}
