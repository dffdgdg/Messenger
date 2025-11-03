using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using Avalonia.Media.Imaging;

namespace MessengerDesktop.ViewModels
{
    public partial class UserProfileDialogViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient = new() { BaseAddress = new Uri(App.ApiUrl) };
        
        public Action? CloseAction { get; set; }
        public Func<string, Task>? SendMessageAction { get; set; }

        [ObservableProperty]
        private UserDTO? user;

        [ObservableProperty]
        private string? message;

        [ObservableProperty]
        private Bitmap? avatarBitmap;

        public string? AvatarUrl => User?.Avatar == null ? null : $"{App.ApiUrl}{User.Avatar}";

        public UserProfileDialogViewModel(UserDTO user)
        {
            this.user = user;
            _ = LoadAvatarAsync();
        }

        private async Task LoadAvatarAsync()
        {
            if (string.IsNullOrEmpty(AvatarUrl))
            {
                AvatarBitmap = null;
                return;
            }

            try
            {
                using var response = await _httpClient.GetAsync(AvatarUrl);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
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
        }

        [RelayCommand]
        private void Close()
        {
            CloseAction?.Invoke();
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                if (SendMessageAction != null)
                {
                    await SendMessageAction(message);
                    Close();
                }
            }
            catch (Exception ex)
            {
                await NotificationService.ShowError($"Ошибка отправки сообщения: {ex.Message}");
            }
        }

        partial void OnUserChanged(UserDTO? value)
        {
            OnPropertyChanged(nameof(AvatarUrl));
            _ = LoadAvatarAsync();
        }
    }
}