using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerShared.Dto.User;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class UserProfileDialogViewModel : DialogBaseViewModel
{
    private readonly IApiClientService _apiClient;

    [ObservableProperty]
    private UserDto _user;

    [ObservableProperty]
    private Bitmap? _avatarBitmap;

    [ObservableProperty]
    private string _department = string.Empty;

    /// <summary>
    /// Callback для открытия/создания чата с пользователем
    /// </summary>
    public Func<UserDto, Task>? OpenChatWithUserAction { get; set; }

    public string? AvatarUrl => GetAbsoluteUrl(User.Avatar);

    /// <summary>
    /// Показывать ли кнопку "Написать" (скрываем для своего профиля)
    /// </summary>
    public bool CanSendMessage { get; init; } = true;

    public UserProfileDialogViewModel(UserDto user, IApiClientService apiClient)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        Title = $"Профиль: {user.DisplayName ?? user.Username}";
        CanCloseOnBackgroundClick = true;
        Department = user.Department ?? string.Empty;
    }

    [RelayCommand]
    public Task Initialize() => InitializeAsync(LoadAvatarAsync);

    /// <summary>
    /// Команда "Написать сообщение" - открывает или создаёт чат с пользователем
    /// </summary>
    [RelayCommand]
    private async Task SendMessage()
    {
        if (OpenChatWithUserAction == null)
        {
            ErrorMessage = "Действие не настроено";
            return;
        }

        try
        {
            RequestClose();
            await OpenChatWithUserAction.Invoke(User);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка открытия чата: {ex.Message}";
        }
    }

    private async Task LoadAvatarAsync()
    {
        if (string.IsNullOrEmpty(AvatarUrl))
        {
            AvatarBitmap?.Dispose();
            AvatarBitmap = null;
            return;
        }

        try
        {
            await using var stream = await _apiClient.GetStreamAsync(AvatarUrl);
            if (stream == null)
            {
                AvatarBitmap?.Dispose();
                AvatarBitmap = null;
                return;
            }

            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            AvatarBitmap?.Dispose();
            AvatarBitmap = new Bitmap(memoryStream);
        }
        catch
        {
            AvatarBitmap?.Dispose();
            AvatarBitmap = null;
        }
    }

    partial void OnUserChanged(UserDto value)
    {
        OnPropertyChanged(nameof(AvatarUrl));
        Title = $"Профиль: {value.DisplayName ?? value.Username}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AvatarBitmap?.Dispose();
            AvatarBitmap = null;
        }
        base.Dispose(disposing);
    }
}