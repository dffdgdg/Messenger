using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MessengerDesktop.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class ChatEditDialogViewModel : DialogBaseViewModel
{
    private const int MaxAvatarSizeMb = 5;

    private readonly IApiClientService _apiClient;
    private readonly int _currentUserId;
    private readonly ChatDto? _originalChat;
    private readonly List<ChatMemberDto>? _existingMembers;

    private MemoryStream? _avatarStream;
    private string? _avatarFileName;
    private bool _isAvatarRemoved;

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial ObservableCollection<UserListItemViewModel> AvailableUsers { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<UserListItemViewModel> FilteredUsers { get; set; } = [];
    [ObservableProperty] public partial string SearchUserQuery { get; set; } = string.Empty;
    [ObservableProperty] public partial Bitmap? AvatarPreview { get; set; }
    [ObservableProperty] public partial ChatRole CurrentUserRole { get; set; } = ChatRole.Owner;
    [ObservableProperty] public partial ObservableCollection<int> SelectedAdminIds { get; set; } = [];

    public bool IsNewChat => _originalChat == null;
    public int SelectedUsersCount => AvailableUsers.Count(u => u.IsSelected);
    public int ParticipantsCount => AvailableUsers.Count(u => u.IsSelected);
    public int AdminsCount => AvailableUsers.Count(u => u.IsSelected && SelectedAdminIds.Contains(u.Id));
    public bool CanManageParticipants => IsNewChat || CurrentUserRole is ChatRole.Admin or ChatRole.Owner;
    public bool CanManageAdmins => IsNewChat || CurrentUserRole == ChatRole.Owner;
    public bool CanSave => !string.IsNullOrWhiteSpace(Name) && ParticipantsCount >= 1;

    public Func<ChatDto, List<int>, List<int>, Stream?, string?, bool, Task<bool>>? SaveAction { get; set; }

    public Func<DialogBaseViewModel, Task>? ShowDialogAction { get; set; }

    public ChatEditDialogViewModel(IApiClientService apiClient, int currentUserId) : this(apiClient, currentUserId, null) { }

    public ChatEditDialogViewModel(IApiClientService apiClient, int currentUserId, ChatDto? chat, List<ChatMemberDto>? existingMembers = null)
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
        var result = await _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Users.GetAll);

        if (!result.Success || result.Data == null)
        {
            ErrorMessage = $"Ошибка загрузки пользователей: {result.Error}";
            return;
        }

        var existingMemberIds = _existingMembers?.Select(m => m.UserId).ToHashSet() ?? [];
        var adminIds = _existingMembers?.Where(m => m.Role is ChatRole.Admin or ChatRole.Owner).Select(m => m.UserId).ToHashSet() ?? [];
        SelectedAdminIds = new ObservableCollection<int>(adminIds);

        var currentMember = _existingMembers?.FirstOrDefault(m => m.UserId == _currentUserId);
        CurrentUserRole = currentMember?.Role ?? ChatRole.Owner;

        var users = result.Data.Where(u => u.Id != _currentUserId).OrderBy(u => u.DisplayName ?? u.Username)
            .Select(u => new UserListItemViewModel(u, existingMemberIds.Contains(u.Id))).ToList();

        SetAvailableUsers(users);
    }

    private void SetAvailableUsers(List<UserListItemViewModel> users)
    {
        UnsubscribeFromUsers(AvailableUsers);
        AvailableUsers = new ObservableCollection<UserListItemViewModel>(users);
        SubscribeToUsers(AvailableUsers);
        ApplyUserFilter();
        OnPropertyChanged(nameof(SelectedUsersCount));
        OnPropertyChanged(nameof(ParticipantsCount));
        OnPropertyChanged(nameof(AdminsCount));
        NotifyCanSaveChanged();
    }

    private void SubscribeToUsers(IEnumerable<UserListItemViewModel> users)
    {
        foreach (var user in users)
            user.PropertyChanged += OnUserSelectionChanged;
    }

    private void UnsubscribeFromUsers(IEnumerable<UserListItemViewModel> users)
    {
        foreach (var user in users)
            user.PropertyChanged -= OnUserSelectionChanged;
    }

    private void OnUserSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserListItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedUsersCount));
            OnPropertyChanged(nameof(ParticipantsCount));
            OnPropertyChanged(nameof(AdminsCount));
            NotifyCanSaveChanged();
        }
    }

    partial void OnAvailableUsersChanged(ObservableCollection<UserListItemViewModel>? oldValue, ObservableCollection<UserListItemViewModel> newValue)
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
            FilteredUsers = new ObservableCollection<UserListItemViewModel>(AvailableUsers);
            return;
        }

        var query = SearchUserQuery;
        var filtered = AvailableUsers.Where(u =>
            (u.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (u.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

        FilteredUsers = new ObservableCollection<UserListItemViewModel>(filtered);
    }

    [RelayCommand]
    private async Task ManageParticipants()
    {
        if (ShowDialogAction == null)
            return;

        var dialog = new UserListDialogViewModel("Участники",AvailableUsers, CanManageParticipants,items => items.Where(x => x.IsSelected),
            selectedIds =>
            {
                foreach (var user in AvailableUsers)
                    user.IsSelected = selectedIds.Contains(user.Id);

                var selectedSet = selectedIds.ToHashSet();
                SelectedAdminIds = new ObservableCollection<int>(SelectedAdminIds.Where(selectedSet.Contains));
                OnPropertyChanged(nameof(ParticipantsCount));
                OnPropertyChanged(nameof(AdminsCount));
            }, "Изменить состав", "Участники не выбраны");


        await ShowDialogAction(dialog);
    }

    [RelayCommand]
    private async Task ManageAdmins()
    {
        if (ShowDialogAction == null)
            return;

        var adminsSource = AvailableUsers.Where(x => x.IsSelected)
            .Select(x => x.Clone(SelectedAdminIds.Contains(x.Id)))
            .ToList();

        var dialog = new UserListDialogViewModel("Администраторы",
            adminsSource,
            CanManageAdmins,
            items => items.Where(x => x.IsSelected),
            selectedIds =>
            {
                SelectedAdminIds = new ObservableCollection<int>(selectedIds);
                OnPropertyChanged(nameof(AdminsCount));
            }, "Изменить роли", "Администраторы не назначены");


        await ShowDialogAction(dialog);
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

            var memoryStream = new MemoryStream();
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
            if (files.Count == 0) return;
            var file = files[0];

            var path = file.TryGetLocalPath();
            if (path == null) return;

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > MaxAvatarSizeMb * 1024 * 1024)
            {
                ErrorMessage = $"Размер файла не должен превышать {MaxAvatarSizeMb} МБ";
                return;
            }

            if (_avatarStream is not null)
                await _avatarStream.DisposeAsync();

            _avatarStream = new MemoryStream();

            await using var fileStream = File.OpenRead(path);
            await fileStream.CopyToAsync(_avatarStream);
            _avatarStream.Position = 0;
            _isAvatarRemoved = false;

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
        var hadAvatar = AvatarPreview != null;
        _avatarStream?.Dispose();
        _avatarStream = null;
        _avatarFileName = null;
        AvatarPreview?.Dispose();
        AvatarPreview = null;
        _isAvatarRemoved = hadAvatar;
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
            var chatDto = new ChatDto
            {
                Id = _originalChat?.Id ?? 0,
                Name = Name.Trim(),
                Type = ChatType.Chat,
                CreatedById = _currentUserId
            };

            if (SaveAction != null)
            {
                _avatarStream?.Seek(0, SeekOrigin.Begin);

                var success = await SaveAction(chatDto, GetSelectedUserIds(), [.. SelectedAdminIds], _avatarStream, _avatarFileName, _isAvatarRemoved);

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

    partial void OnSelectedAdminIdsChanged(ObservableCollection<int> value) => OnPropertyChanged(nameof(AdminsCount));
}