using System;

namespace MessengerDesktop.ViewModels.Department;

public partial class DepartmentMemberViewModel : ObservableObject
{
    public DepartmentMemberViewModel(UserDto user)
    {
        UserId = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName ?? $"{user.Surname} {user.Name}".Trim();

        if (string.IsNullOrWhiteSpace(DisplayName))
            DisplayName = user.Username;

        AvatarUrl = user.Avatar;
        IsOnline = user.IsOnline;
        LastSeen = user.LastOnline;
    }

    public int UserId { get; }
    public string? Username { get; }
    public string? DisplayName { get; }
    public string? AvatarUrl { get; }

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private DateTime? _lastSeen;

    public string StatusText => IsOnline ? "Онлайн" : FormatLastSeen();
    public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);

    private string FormatLastSeen()
    {
        if (LastSeen == null) return "Не в сети";

        var diff = DateTime.Now - LastSeen.Value;

        return diff.TotalMinutes < 1 ? "Только что" :
               diff.TotalMinutes < 60 ? $"Был {(int)diff.TotalMinutes} мин назад" :
               diff.TotalHours < 24 ? $"Был {(int)diff.TotalHours} ч назад" :
               diff.TotalDays < 2 ? "Был вчера" :
               $"Был {LastSeen.Value:dd.MM.yyyy}";
    }
}