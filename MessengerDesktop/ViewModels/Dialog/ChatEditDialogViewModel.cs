using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO;
using MessengerShared.Enum;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class ChatEditDialogViewModel : DialogBaseViewModel
{
    private const int MaxAvatarSizeMb = 5;

    private readonly IApiClientService _apiClient;
    private readonly int _currentUserId;
    private readonly ChatDTO? _originalChat;
    private readonly List<UserDTO>? _existingMembers;

    private MemoryStream? _avatarStream;
    private string? _avatarFileName;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SelectableUserItem> _availableUsers = [];

    [ObservableProperty]
    private ObservableCollection<SelectableUserItem> _filteredUsers = [];

    [ObservableProperty]
    private string _searchUserQuery = string.Empty;

    [ObservableProperty]
    private Bitmap? _avatarPreview;

    public bool IsNewChat => _originalChat == null;
    public int SelectedUsersCount => AvailableUsers.Count(u => u.IsSelected);
    public bool CanSave => !string.IsNullOrWhiteSpace(Name) && SelectedUsersCount >= 1;

    public Func<ChatDTO, List<int>, Stream?, string?, Task<bool>>? SaveAction { get; set; }

    public ChatEditDialogViewModel(IApiClientService apiClient, int currentUserId) : this(apiClient, currentUserId, null, null) { }

    public ChatEditDialogViewModel(IApiClientService apiClient,int currentUserId,ChatDTO? chat,List<UserDTO>? existingMembers = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _currentUserId = currentUserId;
        _originalChat = chat;
        _existingMembers = existingMembers;

        Title = chat == null ? "Создать группу" : "Редактировать группу";
        Name = chat?.Name ?? string.Empty;
        CanCloseOnBackgroundClick = true;
    }

    [RelayCommand]
    public Task Initialize() => InitializeAsync(async () =>
    {
        IsBusy = true;
        await LoadUsersAsync();

        if (_originalChat?.Avatar != null)
            await LoadExistingAvatarAsync(_originalChat.Avatar);
        IsBusy = false;
    });

    #region Users Loading

    private async Task LoadUsersAsync()
    {
        var result = await _apiClient.GetAsync<List<UserDTO>>("api/user");

        if (!result.Success || result.Data == null)
        {
            ErrorMessage = $"Ошибка загрузки пользователей: {result.Error}";
            return;
        }

        var existingMemberIds = _existingMembers?.Select(m => m.Id).ToHashSet() ?? [];

        var users = result.Data.Where(u => u.Id != _currentUserId).OrderBy(u => u.DisplayName ?? u.Username)
            .Select(u => new SelectableUserItem(u, existingMemberIds.Contains(u.Id))).ToList();

        SetAvailableUsers(users);
    }

    private void SetAvailableUsers(List<SelectableUserItem> users)
    {
        UnsubscribeFromUsers(AvailableUsers);
        AvailableUsers = new ObservableCollection<SelectableUserItem>(users);
        SubscribeToUsers(AvailableUsers);
        ApplyUserFilter();
        NotifyCanSaveChanged();
    }

    private void SubscribeToUsers(IEnumerable<SelectableUserItem> users)
    {
        foreach (var user in users)
            user.PropertyChanged += OnUserSelectionChanged;
    }

    private void UnsubscribeFromUsers(IEnumerable<SelectableUserItem> users)
    {
        foreach (var user in users)
            user.PropertyChanged -= OnUserSelectionChanged;
    }

    private void OnUserSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableUserItem.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedUsersCount));
            NotifyCanSaveChanged();
        }
    }

    partial void OnAvailableUsersChanged(ObservableCollection<SelectableUserItem>? oldValue, ObservableCollection<SelectableUserItem> newValue)
    {
        if (oldValue != null)
            UnsubscribeFromUsers(oldValue);

        SubscribeToUsers(newValue);
        ApplyUserFilter();
        NotifyCanSaveChanged();
    }

    partial void OnSearchUserQueryChanged(string value) => ApplyUserFilter();

    private void ApplyUserFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchUserQuery))
        {
            FilteredUsers = new ObservableCollection<SelectableUserItem>(AvailableUsers);
            return;
        }

        var query = SearchUserQuery;
        var filtered = AvailableUsers.Where(u =>
            (u.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (u.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        FilteredUsers = new ObservableCollection<SelectableUserItem>(filtered);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var user in FilteredUsers)
            user.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var user in AvailableUsers)
            user.IsSelected = false;
    }

    public List<int> GetSelectedUserIds() => [.. AvailableUsers.Where(u => u.IsSelected).Select(u => u.Id)];

    #endregion

    #region Avatar

    private async Task LoadExistingAvatarAsync(string avatarUrl)
    {
        try
        {
            var fullUrl = GetAbsoluteUrl(avatarUrl);
            if (fullUrl == null) return;

            await using var stream = await _apiClient.GetStreamAsync(fullUrl);
            if (stream == null) return;

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            AvatarPreview?.Dispose();
            AvatarPreview = new Bitmap(memoryStream);
        }
        catch
        {
            AvatarPreview?.Dispose();
            AvatarPreview = null;
        }
    }

    [RelayCommand]
    private async Task SelectAvatar()
    {
        try
        {
            var platformService = App.Current?.Services?.GetService<IPlatformService>();
            var storageProvider = platformService?.MainWindow?.StorageProvider;

            if (storageProvider == null)
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

            var files = await storageProvider.OpenFilePickerAsync(options);
            var file = files.FirstOrDefault();
            if (file == null) return;

            var path = file.TryGetLocalPath();
            if (path == null) return;

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > MaxAvatarSizeMb * 1024 * 1024)
            {
                ErrorMessage = $"Размер файла не должен превышать {MaxAvatarSizeMb} МБ";
                return;
            }

            _avatarStream?.Dispose();
            _avatarStream = new MemoryStream();

            await using var fileStream = File.OpenRead(path);
            await fileStream.CopyToAsync(_avatarStream);
            _avatarStream.Position = 0;

            _avatarFileName = Path.GetFileName(path);

            AvatarPreview?.Dispose();
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
        AvatarPreview?.Dispose();
        AvatarPreview = null;
    }

    #endregion

    #region Save

    partial void OnNameChanged(string value)
    {
        if (CanSave) ErrorMessage = null;
        NotifyCanSaveChanged();
    }

    private void NotifyCanSaveChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
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
                Type = ChatType.Chat,
                CreatedById = _currentUserId
            };

            if (SaveAction != null)
            {
                _avatarStream?.Seek(0, SeekOrigin.Begin);

                var success = await SaveAction(chatDto, GetSelectedUserIds(), _avatarStream, _avatarFileName);

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

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeFromUsers(AvailableUsers);
            _avatarStream?.Dispose();
            AvatarPreview?.Dispose();
        }
        base.Dispose(disposing);
    }

    public partial class SelectableUserItem(UserDTO user, bool isSelected = false) : ObservableObject
    {
        public UserDTO User { get; } = user ?? throw new ArgumentNullException(nameof(user));

        [ObservableProperty]
        private bool _isSelected = isSelected;

        public int Id => User.Id;
        public string DisplayName => User.DisplayName ?? User.Username ?? "Пользователь";
        public string Username => $"@{User.Username}";
        public string? AvatarUrl => User.Avatar;
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
    }
}