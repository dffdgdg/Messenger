using Core.Services.Realtime.Abstractions;
using Shared.Contracts.Online;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Features.Shell.MainMenu.Status;

public partial class UserStatusViewModel : BaseViewModel
{
    private readonly IGlobalHubConnection _globalHub;
    private readonly int _userId;

    [ObservableProperty] public partial UserStatusType CurrentStatusType { get; set; } = UserStatusType.Online;
    [ObservableProperty] public partial string CurrentStatusText { get; set; } = "В сети";
    [ObservableProperty] public partial string CurrentStatusColor { get; set; } = "#43A047";

    private static readonly Dictionary<UserStatusType, (string Text, string Color)> StatusMap = new()
    {
        [UserStatusType.Online] = ("В сети", "#43A047"),
        [UserStatusType.Away] = ("Отошёл", "#FFA000"),
        [UserStatusType.Busy] = ("Занят", "#E53935"),
        [UserStatusType.DoNotDisturb] = ("Не беспокоить", "#9C27B0"),
    };

    public void ApplyDto(UserStatusDto status) => OnUserStatusChanged(status);

    public UserStatusViewModel(IGlobalHubConnection globalHub, int userId)
    {
        _globalHub = globalHub;
        _userId = userId;
        _globalHub.UserStatusChanged += OnUserStatusChanged;
    }

    public async Task SetStatusAsync(string param)
    {
        var (status, duration) = param switch
        {
            "Online" => (UserStatusType.Online, (string?)null),
            "Away" => (UserStatusType.Away, null),
            "Busy" => (UserStatusType.Busy, null),
            "DnD" => (UserStatusType.DoNotDisturb, null),
            "Busy15m" => (UserStatusType.Busy, "15m"),
            "Busy30m" => (UserStatusType.Busy, "30m"),
            "Busy1h" => (UserStatusType.Busy, "1h"),
            _ => (UserStatusType.Online, null)
        };

        await _globalHub.SetStatusAsync(status, duration);
    }

    private void OnUserStatusChanged(UserStatusDto status)
    {
        if (status.UserId != _userId) return;

        CurrentStatusType = status.StatusType;

        if (!status.IsOnline)
        {
            CurrentStatusText = "Не в сети";
            CurrentStatusColor = "#9E9E9E";
            return;
        }

        var (text, color) = StatusMap.GetValueOrDefault(status.StatusType, ("В сети", "#43A047"));

        CurrentStatusText = text;
        CurrentStatusColor = color;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _globalHub.UserStatusChanged -= OnUserStatusChanged;
        base.Dispose(disposing);
    }
}
