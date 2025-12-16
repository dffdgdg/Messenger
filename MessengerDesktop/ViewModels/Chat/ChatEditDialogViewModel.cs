using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class ChatEditDialogViewModel : DialogBaseViewModel
    {
        private readonly IApiClientService _apiClient;
        private readonly int _currentUserId;
        private readonly ChatDTO? _originalChat;
        private Stream? _avatarStream;
        private string? _avatarFileName;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private ObservableCollection<SelectableUserItem> _availableUsers = [];

        [ObservableProperty]
        private Bitmap? _avatarPreview;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _searchUserQuery = string.Empty;

        [ObservableProperty]
        private ObservableCollection<SelectableUserItem> _filteredUsers = [];

        public bool IsNewChat => _originalChat == null;

        public int SelectedUsersCount => AvailableUsers.Count(u => u.IsSelected);

        public bool CanSave => !string.IsNullOrWhiteSpace(Name) && SelectedUsersCount >= 1;

        public Func<ChatDTO, List<int>, Stream?, string?, Task<bool>>? SaveAction { get; set; }

        /// <summary>
        /// Конструктор для создания нового группового чата
        /// </summary>
        public ChatEditDialogViewModel(IApiClientService apiClient, int currentUserId)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _currentUserId = currentUserId;
            _originalChat = null;

            Title = "Создать группу";
            CanCloseOnBackgroundClick = true;

            _ = LoadUsersAsync();
        }

        /// <summary>
        /// Конструктор для редактирования существующего группового чата
        /// </summary>
        public ChatEditDialogViewModel(
            IApiClientService apiClient,
            int currentUserId,
            ChatDTO chat,
            List<UserDTO>? existingMembers = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _currentUserId = currentUserId;
            _originalChat = chat ?? throw new ArgumentNullException(nameof(chat));

            Title = "Редактировать группу";
            Name = chat.Name ?? string.Empty;
            CanCloseOnBackgroundClick = true;

            _ = LoadUsersAsync(existingMembers);
            _ = LoadAvatarPreviewAsync(chat.Avatar);
        }

        private async Task LoadUsersAsync(List<UserDTO>? existingMembers = null)
        {
            try
            {
                IsLoading = true;

                var result = await _apiClient.GetAsync<List<UserDTO>>("api/user");

                if (result.Success && result.Data != null)
                {
                    var existingMemberIds = existingMembers?.Select(m => m.Id).ToHashSet() ?? [];

                    var users = result.Data
                        .Where(u => u.Id != _currentUserId)
                        .OrderBy(u => u.DisplayName ?? u.Username)
                        .Select(u => new SelectableUserItem(u)
                        {
                            IsSelected = existingMemberIds.Contains(u.Id)
                        })
                        .ToList();

                    AvailableUsers = new ObservableCollection<SelectableUserItem>(users);

                    foreach (var user in AvailableUsers)
                    {
                        user.PropertyChanged += OnUserSelectionChanged;
                    }

                    ApplyUserFilter();
                    UpdateCanSave();
                }
                else
                {
                    ErrorMessage = $"Ошибка загрузки пользователей: {result.Error}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadAvatarPreviewAsync(string? avatarUrl)
        {
            if (string.IsNullOrEmpty(avatarUrl)) return;

            try
            {
                var fullUrl = avatarUrl.StartsWith("http")
                    ? avatarUrl
                    : $"{App.ApiUrl.TrimEnd('/')}/{avatarUrl.TrimStart('/')}";

                var stream = await _apiClient.GetStreamAsync(fullUrl);

                if (stream != null)
                {
                    AvatarPreview = new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading avatar preview: {ex.Message}");
            }
        }

        private void OnUserSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableUserItem.IsSelected))
            {
                OnPropertyChanged(nameof(SelectedUsersCount));
                UpdateCanSave();
            }
        }

        private void UpdateCanSave()
        {
            OnPropertyChanged(nameof(CanSave));
        }

        partial void OnNameChanged(string value)
        {
            ClearErrorIfValid();
            UpdateCanSave();
        }

        partial void OnSearchUserQueryChanged(string value)
        {
            ApplyUserFilter();
        }

        private void ApplyUserFilter()
        {
            if (string.IsNullOrWhiteSpace(SearchUserQuery))
            {
                FilteredUsers = new ObservableCollection<SelectableUserItem>(AvailableUsers);
            }
            else
            {
                var query = SearchUserQuery.ToLowerInvariant();
                var filtered = AvailableUsers.Where(u =>
                    (u.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (u.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

                FilteredUsers = new ObservableCollection<SelectableUserItem>(filtered);
            }
        }

        private void ClearErrorIfValid()
        {
            if (!string.IsNullOrEmpty(ErrorMessage) && CanSave)
            {
                ErrorMessage = null;
            }
        }

        [RelayCommand]
        private async Task SelectAvatar()
        {
            try
            {
                var platformService = App.Current?.Services?.GetService<IPlatformService>();
                var mainWindow = platformService?.MainWindow;

                if (mainWindow?.StorageProvider == null)
                {
                    ErrorMessage = "Не удалось открыть диалог выбора файла";
                    return;
                }

                var options = new FilePickerOpenOptions
                {
                    Title = "Выберите аватар группы",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Изображения")
                        {
                            Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"],
                            MimeTypes = ["image/jpeg", "image/png", "image/webp"]
                        }
                    ]
                };

                var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
                var file = files.FirstOrDefault();

                if (file == null) return;

                var path = file.TryGetLocalPath();
                if (path == null) return;

                // Проверка размера (макс 5MB)
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > 5 * 1024 * 1024)
                {
                    ErrorMessage = "Размер файла не должен превышать 5 МБ";
                    return;
                }

                // Загружаем превью
                _avatarStream?.Dispose();
                _avatarStream = new MemoryStream();

                await using var fileStream = File.OpenRead(path);
                await fileStream.CopyToAsync(_avatarStream);
                _avatarStream.Position = 0;

                _avatarFileName = Path.GetFileName(path);

                // Создаём превью
                _avatarStream.Position = 0;
                AvatarPreview = new Bitmap(_avatarStream);
                _avatarStream.Position = 0;

                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка выбора файла: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ClearAvatar()
        {
            _avatarStream?.Dispose();
            _avatarStream = null;
            _avatarFileName = null;
            AvatarPreview = null;
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var user in FilteredUsers)
            {
                user.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAll()
        {
            foreach (var user in AvailableUsers)
            {
                user.IsSelected = false;
            }
        }

        [RelayCommand]
        private async Task Save()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                ErrorMessage = "Введите название группы";
                return;
            }

            if (SelectedUsersCount < 1)
            {
                ErrorMessage = "Выберите минимум одного участника";
                return;
            }

            await SafeExecuteAsync(async () =>
            {
                var chatDto = new ChatDTO
                {
                    Id = _originalChat?.Id ?? 0,
                    Name = Name.Trim(),
                    IsGroup = true,
                    CreatedById = _currentUserId
                };

                var selectedUserIds = GetSelectedUserIds();

                if (SaveAction != null)
                {
                    var success = await SaveAction(chatDto, selectedUserIds, _avatarStream, _avatarFileName);

                    if (success)
                    {
                        SuccessMessage = IsNewChat ? "Группа создана" : "Группа обновлена";
                        RequestClose();
                    }
                }
                else
                {
                    RequestClose();
                }
            });
        }

        public List<int> GetSelectedUserIds()
        {
            return [.. AvailableUsers
                .Where(u => u.IsSelected)
                .Select(u => u.User.Id)];
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _avatarStream?.Dispose();
                AvatarPreview?.Dispose();

                foreach (var user in AvailableUsers)
                {
                    user.PropertyChanged -= OnUserSelectionChanged;
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Элемент списка пользователей с возможностью выбора
        /// </summary>
        public partial class SelectableUserItem(UserDTO user) : ObservableObject
        {
            public UserDTO User { get; } = user ?? throw new ArgumentNullException(nameof(user));

            [ObservableProperty]
            private bool _isSelected;

            public int Id => User.Id;
            public string DisplayName => User.DisplayName ?? User.Username ?? "Пользователь";
            public string Username => $"@{User.Username}";
            public string? AvatarUrl => User.Avatar;
            public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        }
    }
}