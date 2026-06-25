using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Core.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Chat;
using Shared.Contracts.User;
using System.ComponentModel;

namespace Core.ViewModels.Dialog;

public partial class ChatEditDialogViewModel : DialogBaseViewModel
{
    private const int MaxAvatarSizeMb = 5;
    private const long MaxAvatarSizeBytes = MaxAvatarSizeMb * 1024 * 1024;

    private readonly IApiClientService _apiClient;
    private readonly IPlatformService _platformService;
    private readonly int _currentUserId;
    private readonly ChatDto? _originalChat;
    private readonly List<ChatMemberDto>? _existingMembers;
    private readonly bool _isSystemAdmin;
    public override bool IsFullscreenInCompactMode => true;

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
    [ObservableProperty] public partial bool ShowHistoryForNewMembers { get; set; }

    public bool IsNewChat => _originalChat == null;
    public bool IsDepartmentScopedChat => _originalChat?.Type is ChatType.Department or ChatType.DepartmentHeads;
    public bool CanDeleteGroup => !IsNewChat && !IsDepartmentScopedChat && (_isSystemAdmin || CurrentUserRole == ChatRole.Owner);
    public bool ShowDeleteGroupOption => CanDeleteGroup;
    public int SelectedUsersCount => AvailableUsers.Count(u => u.IsSelected);
    public int ParticipantsCount => SelectedUsersCount;
    public int AdminsCount => AvailableUsers.Count(u => u.IsSelected && SelectedAdminIds.Contains(u.Id));
    public bool CanManageParticipants => IsNewChat || CurrentUserRole is ChatRole.Admin or ChatRole.Owner;
    public bool CanManageAdmins => IsNewChat || CurrentUserRole == ChatRole.Owner;
    public bool CanEditChatSettings => IsNewChat || CurrentUserRole is ChatRole.Admin or ChatRole.Owner || _isSystemAdmin;
    public string CurrentUserRoleDisplay => CurrentUserRole switch
    {
        ChatRole.Owner => "Владелец",
        _ when _isSystemAdmin => "Тех.админ",
        ChatRole.Admin => "Администратор",
        _ => "Участник"
    };

    public bool CanSave => CanEditChatSettings && !string.IsNullOrWhiteSpace(Name) && ParticipantsCount >= 1;

    public Func<ChatDto, List<int>, List<int>, Stream?, string?, bool, Task<bool>>? SaveAction { get; set; }
    public Func<DialogBaseViewModel, Task>? ShowDialogAction { get; set; }
    public Func<int, Task<bool>>? DeleteAction { get; set; }

    public ChatEditDialogViewModel(IApiClientService apiClient, IPlatformService platformService, int currentUserId, ChatDto? chat = null, 
        List<ChatMemberDto>? existingMembers = null, bool isSystemAdmin = false)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _currentUserId = currentUserId;
        _originalChat = chat;
        _existingMembers = existingMembers;
        _isSystemAdmin = isSystemAdmin;

        Title = chat == null ? "Создать группу" : "Редактировать группу";
        Name = chat?.Name ?? string.Empty;
        CanCloseOnBackgroundClick = true;
    }

    [RelayCommand]
    public Task Initialize() => InitializeAsync(async () =>
    {
        IsBusy = true;
        await LoadUsersAsync();
        if (_originalChat?.Avatar is { } url)
            await LoadAvatarFromUrlAsync(url);
        ShowHistoryForNewMembers = _originalChat?.ShowHistoryForNewMembers ?? true;
        IsBusy = false;
    });


    private async Task LoadUsersAsync()
    {
        var result = await _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Users.GetAll);
        if (!result.Success || result.Data == null)
        {
            ErrorMessage = $"Ошибка загрузки пользователей: {result.Error}";
            return;
        }

        var memberIds = _existingMembers?.Select(m => m.UserId).ToHashSet() ?? [];
        var adminIds = _existingMembers?.Where(m => m.Role is ChatRole.Admin or ChatRole.Owner).Select(m => m.UserId).ToHashSet() ?? [];

        SelectedAdminIds = new ObservableCollection<int>(adminIds);
        CurrentUserRole = _existingMembers?.FirstOrDefault(m => m.UserId == _currentUserId)?.Role ?? ChatRole.Owner;
        OnPropertyChanged(nameof(CurrentUserRoleDisplay));
        OnPropertyChanged(nameof(CanDeleteGroup));
        OnPropertyChanged(nameof(ShowDeleteGroupOption));

        var users = result.Data.Where(u => u.Id != _currentUserId).OrderBy(u => u.DisplayName ?? u.Username).Select(u => new UserListItemViewModel(u, memberIds.Contains(u.Id))).ToList();

        ReplaceAvailableUsers(users);
    }

    private void ReplaceAvailableUsers(List<UserListItemViewModel> users)
    {
        ForEachUser(AvailableUsers, subscribe: false);
        AvailableUsers = new ObservableCollection<UserListItemViewModel>(users);
        ForEachUser(AvailableUsers, subscribe: true);
        ApplyUserFilter();
        NotifySelectionChanged();
    }

    private void ForEachUser(IEnumerable<UserListItemViewModel> users, bool subscribe)
    {
        foreach (var u in users)
        {
            if (subscribe) u.PropertyChanged += OnUserPropertyChanged;
            else u.PropertyChanged -= OnUserPropertyChanged;
        }
    }

    private void OnUserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserListItemViewModel.IsSelected))
            NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedUsersCount));
        OnPropertyChanged(nameof(ParticipantsCount));
        OnPropertyChanged(nameof(AdminsCount));
        NotifyCanSaveChanged();
    }

    private void NotifyCanSaveChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnAvailableUsersChanged(ObservableCollection<UserListItemViewModel> oldValue, ObservableCollection<UserListItemViewModel> newValue)
    {
        if (oldValue != null) ForEachUser(oldValue, subscribe: false);
        ForEachUser(newValue, subscribe: true);
        ApplyUserFilter();
        NotifyCanSaveChanged();
    }

    partial void OnSearchUserQueryChanged(string value) => ApplyUserFilter();

    partial void OnCurrentUserRoleChanged(ChatRole value)
    {
        OnPropertyChanged(nameof(CurrentUserRoleDisplay));
        OnPropertyChanged(nameof(CanDeleteGroup));
        OnPropertyChanged(nameof(ShowDeleteGroupOption));
    }

    private void ApplyUserFilter()
    {
        var source = string.IsNullOrWhiteSpace(SearchUserQuery) ? AvailableUsers : AvailableUsers.Where(u => (u.DisplayName?.Contains(SearchUserQuery, StringComparison.OrdinalIgnoreCase) ?? false)
            || (u.Username?.Contains(SearchUserQuery, StringComparison.OrdinalIgnoreCase) ?? false));

        FilteredUsers = new ObservableCollection<UserListItemViewModel>(source);
    }

    public List<int> GetSelectedUserIds() => [.. AvailableUsers.Where(u => u.IsSelected).Select(u => u.Id)];

    [RelayCommand]
    private Task ManageParticipants() => ShowUserListDialog("Участники", AvailableUsers, CanManageParticipants, items => items.Where(x => x.IsSelected), selectedIds =>
    {
        foreach (var u in AvailableUsers)
            u.IsSelected = selectedIds.Contains(u.Id);

        var set = selectedIds.ToHashSet();
        SelectedAdminIds = new ObservableCollection<int>(SelectedAdminIds.Where(set.Contains));
        NotifySelectionChanged();
    }, "Изменить состав", "Участники не выбраны");

    [RelayCommand]
    private Task ManageAdmins() => ShowUserListDialog("Администраторы", AvailableUsers.Where(x => x.IsSelected).Select(x => x.Clone(SelectedAdminIds.Contains(x.Id))).ToList(), CanManageAdmins,
        items => items.Where(x => x.IsSelected), ids =>
        {
            SelectedAdminIds = new ObservableCollection<int>(ids);
            OnPropertyChanged(nameof(AdminsCount));
        }, "Изменить роли", "Администраторы не назначены");

    private Task ShowUserListDialog<T>(string title, T source, bool canManage, Func<IEnumerable<UserListItemViewModel>, IEnumerable<UserListItemViewModel>> filter, Action<List<int>> onConfirm,
        string confirmText, string emptyText) where T : IEnumerable<UserListItemViewModel>
    {
        if (ShowDialogAction == null) return Task.CompletedTask;

        var dialog = new UserListDialogViewModel(title, source, canManage, filter, onConfirm, confirmText, emptyText);

        return ShowDialogAction(dialog);
    }

    private async Task LoadAvatarFromUrlAsync(string avatarUrl)
    {
        try
        {
            var fullUrl = GetAbsoluteUrl(avatarUrl);
            if (fullUrl == null) return;

            await using var stream = await _apiClient.GetStreamAsync(fullUrl);
            if (stream == null) return;

            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            SetAvatarPreview(new Bitmap(ms));
        }
        catch { SetAvatarPreview(null); }
    }

    [RelayCommand]
    private async Task SelectAvatar()
    {
        try
        {
            var files = await _platformService.OpenFilePickerAsync(new FilePickerOpenOptions
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
            });

            if (files.Count == 0) return;

            var path = files[0].TryGetLocalPath();
            if (path == null) return;

            if (new FileInfo(path).Length > MaxAvatarSizeBytes)
            {
                ErrorMessage = $"Размер файла не должен превышать {MaxAvatarSizeMb} МБ";
                return;
            }

            if (_avatarStream != null) await _avatarStream.DisposeAsync();
            _avatarStream = new MemoryStream();

            await using var fs = File.OpenRead(path);
            await fs.CopyToAsync(_avatarStream);
            _avatarStream.Position = 0;

            _avatarFileName = Path.GetFileName(path);
            _isAvatarRemoved = false;

            SetAvatarPreview(new Bitmap(_avatarStream));
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
        _isAvatarRemoved = AvatarPreview != null;
        SetAvatarPreview(null);
    }

    private void SetAvatarPreview(Bitmap? bitmap)
    {
        AvatarPreview?.Dispose();
        AvatarPreview = bitmap;
    }
    [RelayCommand]
    private async Task DeleteGroup()
    {
        if (IsNewChat || _originalChat == null || DeleteAction == null || ShowDialogAction == null)
            return;

        if (!CanDeleteGroup)
        {
            ErrorMessage = "Удалить группу может только владелец или тех.админ.";
            return;
        }

        var name = _originalChat.Name ?? Name;
        var confirmDialog = new ConfirmDialogViewModel("Удаление группы",
            $"Вы уверены, что хотите удалить группу «{name}»? Это действие нельзя отменить.", "Удалить", "Отмена")
        {
            ConfirmationPrompt = "Для удаления введите название группы точно как указано:",
            ConfirmationTargetText = name
        };

        await ShowDialogAction(confirmDialog);
        var confirmed = await confirmDialog.Result;

        if (!confirmed)
            return;

        IsBusy = true;
        ErrorMessage = null;

        var deleted = await DeleteAction(_originalChat.Id);
        IsBusy = false;

        if (deleted)
            await RequestCloseAsync();
    }

    partial void OnNameChanged(string value)
    {
        if (CanSave) ErrorMessage = null;
        NotifyCanSaveChanged();
    }

    partial void OnSelectedAdminIdsChanged(ObservableCollection<int> value) => OnPropertyChanged(nameof(AdminsCount));

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Введите название группы"; return; }
        if (SelectedUsersCount < 1) { ErrorMessage = "Выберите минимум одного участника"; return; }
        if (!CanEditChatSettings)
        {
            ErrorMessage = "У вас нет прав на изменение настроек чата. Только администратор или владелец может редактировать чат.";
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var chatDto = new ChatDto
            {
                Id = _originalChat?.Id ?? 0,
                Name = Name.Trim(),
                Type = ChatType.Chat,
                CreatedById = _currentUserId,
                ShowHistoryForNewMembers = ShowHistoryForNewMembers
            };

            if (SaveAction == null) { await RequestCloseAsync(); return; }

            _avatarStream?.Seek(0, SeekOrigin.Begin);
            if (await SaveAction(chatDto, GetSelectedUserIds(), [.. SelectedAdminIds], _avatarStream, _avatarFileName, _isAvatarRemoved))
            {
                SuccessMessage = IsNewChat ? "Группа создана" : "Группа обновлена";
                await RequestCloseAsync();
            }
        });
    }

    public void ForceCloseDueToRoleChange()
    {
        ErrorMessage = "Ваши права были изменены. Редактирование чата недоступно.";
        _ = Task.Delay(1500).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() => _ = RequestCloseAsync()));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ForEachUser(AvailableUsers, subscribe: false);
            _avatarStream?.Dispose();
            AvatarPreview?.Dispose();
        }
        base.Dispose(disposing);
    }
}