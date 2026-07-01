using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Infrastructure.Media;
using Core.Services.Api.Abstraction;
using Core.Services.Auth.Abstractions;
using Core.Services.Auth;
using Core.Services.Platform.Abstractions;
using Core.Services.Realtime.Abstractions;
using Core.Shared.Configuration;
using Core.Shared.Helpers;
using Core.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Online;
using Shared.Contracts.User;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Core.Features.Profile.ViewModels;

public partial class ProfileViewModel : BaseViewModel, IRefreshable
{
    private readonly IApiClientService _api;
    private readonly IAuthManager _authManager;
    private readonly INotificationService _notificationService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullName), nameof(Username), nameof(SurnameDisplay), nameof(NameDisplay), nameof(MidnameDisplay),
        nameof(HasDepartment), nameof(DepartmentDisplay), nameof(HasAvatar))]
    public partial UserDto? User { get; set; }
    private readonly IPlatformService _platformService;
    [ObservableProperty] public partial int UserId { get; set; }
    [ObservableProperty] public partial bool IsEditingProfile { get; set; }
    [ObservableProperty] public partial bool IsEditingUsername { get; set; }
    [ObservableProperty] public partial bool IsEditingPassword { get; set; }
    private readonly IGlobalHubConnection _globalHub;

    [ObservableProperty]
    public partial UserStatusType CurrentStatusType { get; set; } = UserStatusType.Online;

    [ObservableProperty]
    public partial string CurrentStatusText { get; set; } = "В сети";

    [ObservableProperty]
    public partial string CurrentStatusColor { get; set; } = "#43A047";

    [ObservableProperty]
    public partial string? SelectedDuration { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TempFullName))]
    public partial string TempSurname { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TempFullName))]
    public partial string TempName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TempFullName))]
    public partial string TempMidname { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveUsername))]
    [NotifyPropertyChangedFor(nameof(UsernameValidationMessage))]
    [NotifyPropertyChangedFor(nameof(IsUsernameValid))]
    public partial string TempUsername { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePassword))]
    public partial string CurrentPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePassword))]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(IsNewPasswordValid))]
    [NotifyPropertyChangedFor(nameof(NewPasswordValidationMessage))]
    [NotifyPropertyChangedFor(nameof(NewPasswordStrength))]
    [NotifyPropertyChangedFor(nameof(NewPasswordStrengthLabel))]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePassword))]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(ShowPasswordMatchIndicator))]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty] public partial Bitmap? AvatarBitmap { get; set; }
    [ObservableProperty] public partial string? AvatarUrl { get; set; }

    public string FullName => FormatFullName(User?.Surname, User?.Name, User?.Midname) ?? User?.Username ?? "Пользователь";
    public string TempFullName => FormatFullName(TempSurname, TempName, TempMidname) ?? "—";
    public string Username => User?.Username ?? string.Empty;
    public string SurnameDisplay => string.IsNullOrWhiteSpace(User?.Surname) ? "Не указана" : User.Surname;
    public string NameDisplay => string.IsNullOrWhiteSpace(User?.Name) ? "Не указано" : User.Name;
    public string MidnameDisplay => string.IsNullOrWhiteSpace(User?.Midname) ? "Не указано" : User.Midname;
    public bool HasDepartment => !string.IsNullOrWhiteSpace(User?.Department);
    public string DepartmentDisplay => User?.Department ?? string.Empty;
    public bool HasAvatar => !string.IsNullOrWhiteSpace(User?.Avatar);

    public bool IsUsernameValid => string.IsNullOrEmpty(TempUsername) || UsernameRegex().IsMatch(TempUsername.Trim());

    public bool CanSaveUsername => !string.IsNullOrWhiteSpace(TempUsername) && TempUsername.Trim().Length >= 3 && IsUsernameValid;

    public bool IsNewPasswordValid => string.IsNullOrEmpty(NewPassword) || NewPassword.Length >= 6;

    public bool ShowPasswordMatchIndicator => !string.IsNullOrEmpty(ConfirmPassword);

    public int NewPasswordStrength => PasswordHelper.CalculateStrength(NewPassword);
    public string NewPasswordStrengthLabel => PasswordHelper.ToStrengthLabel(NewPasswordStrength);

    public bool PasswordsMatch => !string.IsNullOrEmpty(ConfirmPassword) && NewPassword == ConfirmPassword;

    public bool CanSavePassword => !string.IsNullOrWhiteSpace(CurrentPassword) && !string.IsNullOrWhiteSpace(NewPassword) && NewPassword.Length >= 6
        && PasswordsMatch;

    public string? UsernameValidationMessage => TempUsername switch
    {
        "" => null,
        _ when TempUsername.Trim().Length < 3 => "Минимум 3 символа",
        _ when !IsUsernameValid => "Только латинские буквы, цифры и _",
        _ => null
    };

    public string? NewPasswordValidationMessage => NewPassword switch
    {
        "" => null,
        _ when NewPassword.Length < 6 => $"Ещё {6 - NewPassword.Length} символов",
        _ => null
    };

    IAsyncRelayCommand IRefreshable.RefreshCommand => RefreshCommand;

    public ProfileViewModel(IApiClientService apiClient, IAuthManager authManager, INotificationService notificationService, 
        IGlobalHubConnection globalHub, IPlatformService platformService)
    {
        _api = apiClient;
        _authManager = authManager;
        _notificationService = notificationService;
        _globalHub = globalHub;
        _platformService = platformService;
        UserId = authManager.Session.UserId ?? throw new InvalidOperationException("Пользователь не авторизован");

        _globalHub.UserStatusChanged += OnUserStatusChanged;
        _ = LoadUser();
    }

    private static string? FormatFullName(string? surname, string? name, string? midname)
    {
        var parts = new[] { surname, name, midname }.Where(s => !string.IsNullOrWhiteSpace(s));
        return parts.Any() ? string.Join(" ", parts) : null;
    }

    [RelayCommand]
    private async Task Refresh() => await LoadUser();

    private async Task LoadUser() => await SafeExecuteAsync(async () =>
    {
        var r = await _api.GetAsync<UserDto>(ApiEndpoints.Users.ById(UserId));
        if (!r.Success) return;
        User = r.Data;
        RefreshAvatarUrl();
        await LoadAvatarAsync();
        var statusResult = await _api.GetAsync<UserStatusDto>(ApiEndpoints.Users.Status(UserId));
        if (statusResult.Success && statusResult.Data is not null)
            OnUserStatusChanged(statusResult.Data);
    });

    private async Task LoadAvatarAsync()
    {
        AvatarBitmap?.Dispose();
        AvatarBitmap = null;

        if (string.IsNullOrEmpty(AvatarUrl)) return;

        try
        {
            await using var stream = await _api.GetStreamAsync(AvatarUrl);
            if (stream != null) AvatarBitmap = new Bitmap(stream);
        }
        catch { /* avatar load failed silently */ }
    }

    private void RefreshAvatarUrl(bool forceCacheBuster = false)
    {
        var url = forceCacheBuster && !string.IsNullOrEmpty(User?.Avatar)
            ? AvatarHelper.WithFreshCacheBuster(User.Avatar)
            : GetAbsoluteUrl(User?.Avatar);

        Debug.WriteLine($"[ProfileVM] RefreshAvatarUrl: forceBuster={forceCacheBuster}, avatar={User?.Avatar}, result={url}");
        AvatarUrl = url;
    }

    #region Profile editing

    [RelayCommand]
    private void StartEditProfile()
    {
        CancelAllEditing();
        TempSurname = User?.Surname ?? string.Empty;
        TempName = User?.Name ?? string.Empty;
        TempMidname = User?.Midname ?? string.Empty;
        IsEditingProfile = true;
    }

    [RelayCommand]
    private void CancelEditProfile() => IsEditingProfile = false;

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (User == null) return;
        if (string.IsNullOrWhiteSpace(TempSurname) && string.IsNullOrWhiteSpace(TempName))
        {
            await _notificationService.ShowErrorAsync("Укажите хотя бы имя или фамилию");
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var result = await _api.PutAsync<object>(ApiEndpoints.Users.ById(User.Id), new UserDto
            {
                Id = User.Id,
                Username = User.Username,
                Surname = TempSurname.Trim(),
                Name = TempName.Trim(),
                Midname = TempMidname.Trim(),
                Avatar = User.Avatar,
                Department = User.Department
            });

            if (result.Success)
            {
                await LoadUser();
                IsEditingProfile = false;
                await _notificationService.ShowSuccessAsync("Профиль обновлён");
            }
            else
            {
                await _notificationService.ShowErrorAsync(result.Error ?? "Не удалось обновить профиль");
            }
        });
    }

    private void OnUserStatusChanged(UserStatusDto status)
    {
        if (status.UserId != UserId) return;

        CurrentStatusType = status.StatusType;
        CurrentStatusText = status.IsOnline ? status.StatusType switch
        {
            UserStatusType.Online => "В сети",
            UserStatusType.Away => "Отошёл",
            UserStatusType.Busy => "Занят",
            UserStatusType.DoNotDisturb => "Не беспокоить",
            _ => "В сети"
        } : "Не в сети";

        CurrentStatusColor = status.IsOnline ? status.StatusType switch
        {
            UserStatusType.Online => "#43A047",
            UserStatusType.Away => "#FFA000",
            UserStatusType.Busy => "#E53935",
            UserStatusType.DoNotDisturb => "#9C27B0",
            _ => "#43A047"
        } : "#9E9E9E";
    }

    [RelayCommand]
    private async Task SetStatus(string param)
    {
        var (status, duration) = param switch
        {
            "Online" => (UserStatusType.Online, (string?)null),
            "Away" => (UserStatusType.Away, (string?)null),
            "Busy" => (UserStatusType.Busy, (string?)null),
            "DnD" => (UserStatusType.DoNotDisturb, (string?)null),
            "Busy15m" => (UserStatusType.Busy, "15m"),
            "Busy30m" => (UserStatusType.Busy, "30m"),
            "Busy1h" => (UserStatusType.Busy, "1h"),
            "DnD1h" => (UserStatusType.DoNotDisturb, "1h"),
            "DnD2h" => (UserStatusType.DoNotDisturb, "2h"),
            _ => (UserStatusType.Online, (string?)null)
        };

        OnUserStatusChanged(new UserStatusDto(UserId, true, null, status, null));

        await _globalHub.SetStatusAsync(status, duration);
    }

    [RelayCommand]
    private void SetDuration(string duration) => SelectedDuration = duration;
    #endregion

    #region Username editing

    [RelayCommand]
    private void StartEditUsername()
    {
        CancelAllEditing();
        TempUsername = User?.Username ?? string.Empty;
        IsEditingUsername = true;
    }

    [RelayCommand]
    private void CancelEditUsername()
    {
        IsEditingUsername = false;
        TempUsername = string.Empty;
    }

    [RelayCommand]
    private async Task SaveUsername()
    {
        if (User == null || !CanSaveUsername) return;

        var newUsername = TempUsername.Trim().ToLower(new CultureInfo("en-US", false));
        if (newUsername == User.Username?.ToLower(new CultureInfo("en-US", false)))
        {
            IsEditingUsername = false;
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var r = await _api.PutAsync<object>(ApiEndpoints.Users.Username(User.Id), new ChangeUsernameDto { NewUsername = newUsername });

            if (r.Success)
            {
                User.Username = newUsername;
                OnPropertyChanged(nameof(User));
                IsEditingUsername = false;
                await _notificationService.ShowSuccessAsync("Username успешно изменён");
            }
            else
            {
                await _notificationService.ShowErrorAsync(r.Error ?? "Не удалось изменить username");
            }
        });
    }

    #endregion

    #region Password change

    [RelayCommand]
    private void StartEditPassword()
    {
        CancelAllEditing();
        IsEditingPassword = true;
    }

    [RelayCommand]
    private void CancelEditPassword() => IsEditingPassword = false;

    [RelayCommand]
    private async Task SavePassword()
    {
        if (User == null || !CanSavePassword) return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _api.PutAsync<object>(ApiEndpoints.Users.Password(User.Id), new ChangePasswordDto { CurrentPassword = CurrentPassword, NewPassword = NewPassword });

            if (result.Success)
            {
                IsEditingPassword = false;
                CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
                await _notificationService.ShowSuccessAsync("Пароль успешно изменён");
            }
            else
            {
                await _notificationService.ShowErrorAsync(result.Error ?? "Не удалось изменить пароль");
            }
        });
    }

    #endregion

    #region Avatar

    [RelayCommand]
    private async Task UploadAvatar()
    {
        var files = await _platformService.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count == 0) return;

        await SafeExecuteAsync(async () =>
        {
            await using var stream = await files[0].OpenReadAsync();

            // Правильный тип — AvatarResponseDto, не UserDto
            var result = await _api.UploadFileAsync<AvatarResponseDto>(
                ApiEndpoints.Users.Avatar(User!.Id), stream, files[0].Name, "image/png");

            Debug.WriteLine($"[ProfileVM] UploadAvatar: Success={result.Success}, AvatarUrl='{result.Data?.AvatarUrl}'");

            if (!result.Success) return;

            var newAvatarUrl = result.Data!.AvatarUrl; // абсолютный URL с сервера

            var oldAvatar = User!.Avatar;
            if (!string.IsNullOrEmpty(oldAvatar))
            {
                AppConfig.Services.GetRequiredService<AuthenticatedImageLoader>().InvalidateByRelativePath(oldAvatar);
            }

            if (!string.IsNullOrEmpty(newAvatarUrl))
            {
                AppConfig.Services.GetRequiredService<AuthenticatedImageLoader>().InvalidateByRelativePath(newAvatarUrl);
            }

            // Подавляем OnUserChanged чтобы не перезаписал AvatarUrl без buster
            _suppressAvatarRefresh = true;
            try
            {
                User!.Avatar = newAvatarUrl;
                OnPropertyChanged(nameof(HasAvatar));
            }
            finally
            {
                _suppressAvatarRefresh = false;
            }

            // AvatarUrl с timestamp buster для форсированной перезагрузки
            AvatarUrl = AvatarHelper.WithFreshCacheBuster(newAvatarUrl);

            // AvatarBitmap грузим через тот же URL с buster
            AvatarBitmap?.Dispose();
            AvatarBitmap = null;
            try
            {
                await using var imgStream = await _api.GetStreamAsync(AvatarUrl);
                if (imgStream != null) AvatarBitmap = new Bitmap(imgStream);
            }
            catch { /* silent */ }

            await _notificationService.ShowSuccessAsync("Аватар обновлён");
        });
    }

    private bool _suppressAvatarRefresh;

    partial void OnUserChanged(UserDto? value)
    {
        if (_suppressAvatarRefresh) return;
        RefreshAvatarUrl();
    }

    [RelayCommand]
    private async Task DeleteAvatar()
    {
        if (User == null || !HasAvatar) return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _api.DeleteAsync(ApiEndpoints.Users.Avatar(User.Id));
            if (result.Success)
            {
                if (!string.IsNullOrEmpty(User.Avatar))
                    AppConfig.Services.GetRequiredService<AuthenticatedImageLoader>().InvalidateByRelativePath(User.Avatar);

                User.Avatar = null;
                AvatarBitmap?.Dispose();
                AvatarBitmap = null;
                RefreshAvatarUrl();
                OnPropertyChanged(nameof(HasAvatar));
                await _notificationService.ShowSuccessAsync("Аватар удалён");
            }
            else
            {
                await _notificationService.ShowErrorAsync(result.Error ?? "Не удалось удалить аватар");
            }
        });
    }

    #endregion

    #region Helpers

    private void CancelAllEditing()
    {
        IsEditingProfile = IsEditingUsername = IsEditingPassword = false;
        TempSurname = TempName = TempMidname = string.Empty;
        TempUsername = string.Empty;
        CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
    }

    [RelayCommand]
    private async Task Logout()
    {
        if (AppConfig.LogoutCallback != null)
            await AppConfig.LogoutCallback();
        else
            await _authManager.LogoutAsync();
    }

    [RelayCommand] protected void ClearError() => ErrorMessage = null;
    [RelayCommand] protected void ClearSuccess() => SuccessMessage = null;

    #endregion

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _globalHub.UserStatusChanged -= OnUserStatusChanged;
            AvatarBitmap?.Dispose();
            AvatarBitmap = null;
        }
        base.Dispose(disposing);
    }

    #endregion

    [GeneratedRegex("^[a-zA-Z0-9_]{3,30}$", RegexOptions.None)]
    private static partial Regex UsernameRegex();
}