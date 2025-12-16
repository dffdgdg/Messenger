using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class UserProfileDialogViewModel : DialogBaseViewModel
    {
        private readonly IApiClientService _apiClient;

        [ObservableProperty]
        private UserDTO? _user;

        [ObservableProperty]
        private string? _message;

        [ObservableProperty]
        private Bitmap? _avatarBitmap;
        public Func<string, Task>? SendMessageAction { get; set; }

        public string? AvatarUrl => User?.Avatar == null 
            ? null 
            : $"{App.ApiUrl.TrimEnd('/')}/{User.Avatar.TrimStart('/')}?v={DateTime.Now.Ticks}";

        public UserProfileDialogViewModel(UserDTO user, IApiClientService apiClient)
        {
            _user = user;
            _apiClient = apiClient;
            Title = $"Профиль: {user.DisplayName ?? user.Username}";
            CanCloseOnBackgroundClick = true;
            _ = LoadAvatarAsync();
        }

        private async Task LoadAvatarAsync()
        {
            if (string.IsNullOrEmpty(AvatarUrl))
            {
                AvatarBitmap = null;
                return;
            }

            await SafeExecuteAsync(async () =>
            {
                try
                {
                    var stream = await _apiClient.GetStreamAsync(AvatarUrl);

                    if (stream != null)
                    {
                        AvatarBitmap = new Bitmap(stream);
                    }
                    else
                    {
                        AvatarBitmap = null;
                    }
                }
                catch (Exception)
                {
                    AvatarBitmap = null;
                }
            });
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(Message))
            {
                ErrorMessage = "Введите сообщение";
                return;
            }

            await SafeExecuteAsync(async () =>
            {
                if (SendMessageAction != null)
                {
                    await SendMessageAction(Message);
                    SuccessMessage = "Сообщение отправлено";
                    RequestClose();
                }
            });
        }

        partial void OnUserChanged(UserDTO? value)
        {
            OnPropertyChanged(nameof(AvatarUrl));
            _ = LoadAvatarAsync();
        }

        partial void OnMessageChanged(string? value)
        {
            if (!string.IsNullOrEmpty(ErrorMessage) && !string.IsNullOrWhiteSpace(value))
            {
                ErrorMessage = null;
            }
        }
    }
}