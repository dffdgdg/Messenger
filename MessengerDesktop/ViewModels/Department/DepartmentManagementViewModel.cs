using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Infrastructure.Configuration;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.UI;
using MessengerShared.DTO.Department;
using MessengerShared.DTO.User;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Department;

public partial class DepartmentManagementViewModel : BaseViewModel
{
    private readonly IApiClientService _apiClient;
    private readonly IAuthManager _authManager;
    private readonly INotificationService _notificationService;

    private CancellationTokenSource? _loadingCts;
    private int? _departmentId;

    public Func<int, Task>? NavigateToChatAction { get; set; }
    public Func<UserDTO, Task>? OpenChatWithUserAction { get; set; }
    public Func<DepartmentMemberViewModel, Task<bool>>? ShowRemoveConfirmAction { get; set; }
    public Func<ObservableCollection<UserDTO>, Task<UserDTO?>>? ShowSelectUserAction { get; set; }

    public DepartmentManagementViewModel(IApiClientService apiClient,IAuthManager authManager,INotificationService notificationService)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

        _ = InitializeAsync();
    }

    #region Observable Properties

    [ObservableProperty]
    private DepartmentDTO? _department;

    [ObservableProperty]
    private ObservableCollection<DepartmentMemberViewModel> _members = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredMembers))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canManage;

    [ObservableProperty]
    private ObservableCollection<UserDTO> _availableUsers = [];

    [ObservableProperty]
    private bool _hasNoDepartment;

    [ObservableProperty]
    private string? _noDepartmentMessage;

    #endregion

    #region Computed Properties

    public ObservableCollection<DepartmentMemberViewModel> FilteredMembers
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
                return Members;

            var query = SearchQuery.ToLowerInvariant();
            var filtered = Members.Where(m => MatchesSearch(m, query));
            return new ObservableCollection<DepartmentMemberViewModel>(filtered);
        }
    }

    public int OnlineCount => Members.Count(m => m.IsOnline);
    public int TotalCount => Members.Count;
    public string DepartmentName => Department?.Name ?? "Мой отдел";

    private int CurrentUserId => _authManager.Session.UserId ?? 0;

    #endregion

    #region Initialization

    private async Task InitializeAsync()
    {
        _loadingCts = new CancellationTokenSource();
        await LoadAsync(_loadingCts.Token);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            ClearMessages();

            // Получаем информацию о текущем пользователе
            var userResult = await _apiClient.GetAsync<UserDTO>(ApiEndpoints.User.ById(CurrentUserId), ct);

            if (!userResult.Success || userResult.Data == null)
            {
                ErrorMessage = "Не удалось загрузить данные пользователя";
                return;
            }

            var currentUser = userResult.Data;

            if (currentUser.DepartmentId == null)
            {
                HasNoDepartment = true;
                NoDepartmentMessage = "Вы не состоите в отделе";
                CanManage = false;
                return;
            }

            _departmentId = currentUser.DepartmentId.Value;
            HasNoDepartment = false;

            // Проверяем права на управление
            var canManageResult = await _apiClient.GetAsync<bool>(ApiEndpoints.Department.CanManage((int)_departmentId), ct);
            CanManage = canManageResult is { Success: true, Data: true };

            if (!CanManage)
            {
                ErrorMessage = "У вас нет прав на управление этим отделом";
                return;
            }

            // Загружаем информацию об отделе
            var departmentResult = await _apiClient.GetAsync<DepartmentDTO>(ApiEndpoints.Department.ById((int)_departmentId), ct);
            if (departmentResult is { Success: true, Data: not null })
            {
                Department = departmentResult.Data;
            }

            // Загружаем сотрудников
            await LoadMembersAsync(ct);

            // Загружаем доступных пользователей
            await LoadAvailableUsersAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Загрузка отменена
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadMembersAsync(CancellationToken ct)
    {
        if (_departmentId == null) return;

        var membersResult = await _apiClient.GetAsync<List<UserDTO>>(ApiEndpoints.Department.Members((int)_departmentId), ct);

        if (membersResult is { Success: true, Data: not null })
        {
            var memberVms = membersResult.Data.Where(u => u.Id != CurrentUserId) // Исключаем себя
                .Select(u => new DepartmentMemberViewModel(u)).OrderByDescending(m => m.IsOnline).ThenBy(m => m.DisplayName);

            Members = new ObservableCollection<DepartmentMemberViewModel>(memberVms);

            OnPropertyChanged(nameof(FilteredMembers));
            OnPropertyChanged(nameof(OnlineCount));
            OnPropertyChanged(nameof(TotalCount));
        }
        else
        {
            ErrorMessage = $"Ошибка загрузки сотрудников: {membersResult.Error}";
        }
    }

    private async Task LoadAvailableUsersAsync(CancellationToken ct)
    {
        var usersResult = await _apiClient.GetAsync<List<UserDTO>>(ApiEndpoints.User.GetAll, ct);

        if (usersResult is { Success: true, Data: not null })
        {
            var available = usersResult.Data.Where(u => u.DepartmentId == null && !u.IsBanned && u.Id != CurrentUserId).ToList();

            AvailableUsers = new ObservableCollection<UserDTO>(available);
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task Refresh()
    {
        if (_loadingCts is not null)
            await _loadingCts.CancelAsync();

        _loadingCts = new CancellationTokenSource();
        await LoadAsync(_loadingCts.Token);
    }

    [RelayCommand]
    private async Task AddMember()
    {
        if (_departmentId == null || !CanManage) return;

        if (AvailableUsers.Count == 0)
        {
            await _notificationService.ShowInfoAsync("Нет доступных пользователей для добавления");
            return;
        }

        // Показываем диалог выбора пользователя
        var selectedUser = ShowSelectUserAction != null ? await ShowSelectUserAction(AvailableUsers) : null;

        if (selectedUser == null) return;

        await SafeExecuteAsync(async ct =>
        {
            var dto = new UpdateDepartmentMemberDTO { UserId = selectedUser.Id };
            var result = await _apiClient.PostAsync(ApiEndpoints.Department.Members((int)_departmentId), dto, ct);

            if (result.Success)
            {
                await _notificationService.ShowSuccessAsync($"Сотрудник {selectedUser.DisplayName ?? selectedUser.Username} добавлен в отдел");
                await LoadAsync(ct);
            }
            else
            {
                ErrorMessage = $"Ошибка добавления: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task RemoveMember(DepartmentMemberViewModel? member)
    {
        if (member == null || _departmentId == null || !CanManage) return;

        // Показываем подтверждение
        var confirmed = ShowRemoveConfirmAction != null && await ShowRemoveConfirmAction(member);
        if (!confirmed) return;

        await SafeExecuteAsync(async ct =>
        {
            var result = await _apiClient.DeleteAsync(ApiEndpoints.Department.RemoveMember((int)_departmentId, member.UserId), ct);

            if (result.Success)
            {
                Members.Remove(member);

                OnPropertyChanged(nameof(FilteredMembers));
                OnPropertyChanged(nameof(OnlineCount));
                OnPropertyChanged(nameof(TotalCount));

                await _notificationService.ShowSuccessAsync($"Сотрудник {member.DisplayName} удалён из отдела");
                await LoadAvailableUsersAsync(ct);
            }
            else
            {
                ErrorMessage = $"Ошибка удаления: {result.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task OpenChat(DepartmentMemberViewModel? member)
    {
        if (member == null) return;

        if (OpenChatWithUserAction != null)
        {
            var user = new UserDTO
            {
                Id = member.UserId,
                Username = member.Username,
                DisplayName = member.DisplayName,
                Avatar = member.AvatarUrl
            };
            await OpenChatWithUserAction(user);
        }
    }
    #endregion

    #region Helpers

    private static bool MatchesSearch(DepartmentMemberViewModel member, string query) =>
        member.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        member.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredMembers));

    private new void ClearMessages()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }

    #endregion

    #region Cleanup

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadingCts?.Cancel();
            _loadingCts?.Dispose();
            _loadingCts = null;
        }
        base.Dispose(disposing);
    }

    #endregion
}