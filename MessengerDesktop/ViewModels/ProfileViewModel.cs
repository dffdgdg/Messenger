using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MessengerDesktop.Infrastructure;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class ProfileViewModel : BaseViewModel, IRefreshable
{
    private readonly IApiClientService _api;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullName), nameof(Username), nameof(SurnameDisplay), nameof(NameDisplay), nameof(MidnameDisplay),
        nameof(HasDepartment), nameof(DepartmentDisplay), nameof(HasAvatar))]
    public partial UserDto? User { get; set; }

    [ObservableProperty] public partial int UserId { get; set; }
    [ObservableProperty] public partial bool IsEditingProfile { get; set; }
    [ObservableProperty] public partial bool IsEditingUsername { get; set; }
    [ObservableProperty] public partial bool IsEditingPassword { get; set; }

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
    [NotifyPropertyChangedFor(nameof(CanSaveUsername), nameof(UsernameValidationMessage), nameof(IsUsernameValid))]
    public partial string TempUsername { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePassword))]
    public partial string CurrentPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePassword), nameof(PasswordsMatch), nameof(IsNewPasswordValid), nameof(NewPasswordValidationMessage))]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSavePassword), nameof(PasswordsMatch), nameof(ShowPasswordMatchIndicator))]
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
    public bool PasswordsMatch => NewPassword == ConfirmPassword;
    public bool ShowPasswordMatchIndicator => !string.IsNullOrEmpty(ConfirmPassword);

    public bool CanSavePassword => !string.IsNullOrWhiteSpace(CurrentPassword)
        && !string.IsNullOrWhiteSpace(NewPassword) && NewPassword.Length >= 6 && PasswordsMatch;

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

    public ProfileViewModel(IApiClientService apiClient, IAuthManager authManager)
    {
        _api = apiClient;
        UserId = authManager.Session.UserId ?? throw new InvalidOperationException("User not authenticated");
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

    partial void OnUserChanged(UserDto? value) => RefreshAvatarUrl();

    private void RefreshAvatarUrl(bool forceCacheBuster = false) =>
        AvatarUrl = forceCacheBuster ? AvatarHelper.GetUrlWithCacheBuster(User?.Avatar) : GetAbsoluteUrl(User?.Avatar);

    #region Profile editing

    [RelayCommand]
    private void StartEditProfile()
    {
        CancelAllEditing();
        TempSurname = User?.Surname ?? "";
        TempName = User?.Name ?? "";
        TempMidname = User?.Midname ?? "";
        IsEditingProfile = true;
    }

    [RelayCommand]
    private void CancelEditProfile() => IsEditingProfile = false;

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (User == null) return;
        if (string.IsNullOrWhiteSpace(TempSurname) && string.IsNullOrWhiteSpace(TempName))
        { ErrorMessage = "Укажите хотя бы имя или фамилию"; return; }

        await SafeExecuteAsync(async () =>
        {
            var r = await _api.PutAsync<UserDto>(ApiEndpoints.Users.ById(User.Id), new UserDto
            {
                Id = User.Id,
                Username = User.Username,
                Surname = TempSurname.Trim(),
                Name = TempName.Trim(),
                Midname = TempMidname.Trim(),
                Avatar = User.Avatar,
                Department = User.Department
            });
            if (r.Success)
            {
                User.Surname = TempSurname.Trim();
                User.Name = TempName.Trim();
                User.Midname = TempMidname.Trim();
                OnPropertyChanged(nameof(FullName));
                IsEditingProfile = false;
                SuccessMessage = "Профиль обновлён";
            }
            else { ErrorMessage = r.Error; }
        });
    }

    #endregion

    #region Username editing

    [RelayCommand]
    private void StartEditUsername()
    {
        CancelAllEditing();
        TempUsername = User?.Username ?? "";
        IsEditingUsername = true;
    }

    [RelayCommand]
    private void CancelEditUsername() { IsEditingUsername = false; TempUsername = ""; }

    [RelayCommand]
    private async Task SaveUsername()
    {
        if (User == null || !CanSaveUsername) return;
        var newUsername = TempUsername.Trim().ToLower();
        if (newUsername == User.Username?.ToLower()) { IsEditingUsername = false; return; }

        await SafeExecuteAsync(async () =>
        {
            var r = await _api.PutAsync<object>(ApiEndpoints.Users.Username(User.Id), new ChangeUsernameDto { NewUsername = newUsername });
            if (r.Success)
            {
                User.Username = newUsername;
                OnPropertyChanged(nameof(User));
                IsEditingUsername = false;
                SuccessMessage = "Username успешно изменён";
            }
            else { ErrorMessage = r.Error; }
        });
    }

    #endregion

    #region Password change

    [RelayCommand]
    private void StartEditPassword()
    {
        CancelAllEditing();
        CurrentPassword = NewPassword = ConfirmPassword = "";
        IsEditingPassword = true;
    }

    [RelayCommand]
    private void CancelEditPassword()
    {
        IsEditingPassword = false;
        CurrentPassword = NewPassword = ConfirmPassword = "";
    }

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
                CurrentPassword = NewPassword = ConfirmPassword = "";
                SuccessMessage = "Пароль успешно изменён";
            }
            else { ErrorMessage = result.Error; }
        });
    }

    #endregion

    #region Avatar

    [RelayCommand]
    private async Task UploadAvatar()
    {
        var storage = (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime) ?.MainWindow?.StorageProvider;
        if (storage == null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [FilePickerFileTypes.ImageAll] });
        if (files.Count == 0) return;

        await SafeExecuteAsync(async () =>
        {
            await using var stream = await files[0].OpenReadAsync();
            var result = await _api.UploadFileAsync<UserDto>(ApiEndpoints.Users.Avatar(User!.Id), stream, files[0].Name, "image/png");
            if (!result.Success) return;

            User.Avatar = result.Data!.Avatar;
            RefreshAvatarUrl(forceCacheBuster: true);
            OnPropertyChanged(nameof(HasAvatar));
            await LoadAvatarAsync();
            SuccessMessage = "Аватар обновлён";
        });
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
                User.Avatar = null;
                AvatarBitmap?.Dispose();
                AvatarBitmap = null;
                RefreshAvatarUrl();
                OnPropertyChanged(nameof(HasAvatar));
                SuccessMessage = "Аватар удалён";
            }
            else { ErrorMessage = result.Error ?? "Не удалось удалить аватар"; }
        });
    }

    #endregion

    #region Helpers

    private void CancelAllEditing()
    {
        IsEditingProfile = IsEditingUsername = IsEditingPassword = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private static async Task Logout()
    {
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel main })
            await main.Logout();
    }

    [RelayCommand] protected void ClearError() => ErrorMessage = null;
    [RelayCommand] protected void ClearSuccess() => SuccessMessage = null;

    #endregion

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (disposing) { AvatarBitmap?.Dispose(); AvatarBitmap = null; }
        base.Dispose(disposing);
    }

    #endregion

    [GeneratedRegex("^[a-zA-Z0-9_]{3,30}$", RegexOptions.None)]
    private static partial Regex UsernameRegex();
}