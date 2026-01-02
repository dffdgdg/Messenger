using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.ViewModels.Admin;
using MessengerShared.DTO.Department;
using MessengerShared.DTO.User;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class UsersTabViewModel(IApiClientService apiClient, IDialogService dialogService) : BaseViewModel
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly IDialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

    [ObservableProperty]
    private ObservableCollection<UserDTO> _users = [];

    [ObservableProperty]
    private ObservableCollection<DepartmentGroup> _groupedUsers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGroups))]
    private string _searchQuery = string.Empty;

    private IReadOnlyList<DepartmentDTO> _departments = [];

    public IEnumerable<DepartmentGroup> FilteredGroups => ApplyFilter();

    public void SetDepartments(IReadOnlyList<DepartmentDTO> departments)
    {
        _departments = departments;
        RebuildGroups();
    }

    public async Task LoadAsync() => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.GetAsync<List<UserDTO>>(ApiEndpoints.Admin.Users);

        if (result is { Success: true, Data: not null })
        {
            Users = new ObservableCollection<UserDTO>(result.Data);
            RebuildGroups();
        }
        else
        {
            ErrorMessage = $"Ошибка загрузки пользователей: {result.Error}";
        }
    });

    [RelayCommand]
    private async Task Create()
    {
        var userDialog = new UserEditDialogViewModel(null, new ObservableCollection<DepartmentDTO>(_departments));

        var tcs = new TaskCompletionSource<bool>();

        userDialog.CreateAction = async createDto =>
        {
            await SafeExecuteAsync(async () =>
            {
                var apiResult = await _apiClient.PostAsync<UserDTO>(ApiEndpoints.Admin.Users, createDto);

                if (apiResult.Success)
                {
                    await LoadAsync();
                    SuccessMessage = "Пользователь создан";
                    tcs.TrySetResult(true);
                }
                else
                {
                    userDialog.ErrorMessage = $"Ошибка создания: {apiResult.Error}";
                    tcs.TrySetResult(false);
                }
            });
        };

        userDialog.CloseRequested += () =>
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(false);
        };

        await _dialogService.ShowAsync(userDialog);
        await tcs.Task;
    }

    [RelayCommand]
    private async Task Edit(UserDTO user)
    {
        var userDialog = new UserEditDialogViewModel(user, new ObservableCollection<DepartmentDTO>(_departments));

        var tcs = new TaskCompletionSource<bool>();

        userDialog.UpdateAction = async updateDto =>
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.PutAsync<UserDTO>(ApiEndpoints.User.ById(user.Id), updateDto);

                if (result.Success)
                {
                    await LoadAsync();
                    SuccessMessage = "Пользователь обновлён";
                    tcs.TrySetResult(true);
                }
                else
                {
                    userDialog.ErrorMessage = $"Ошибка обновления: {result.Error}";
                    tcs.TrySetResult(false);
                }
            });
        };

        userDialog.CloseRequested += () =>
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(false);
        };

        await _dialogService.ShowAsync(userDialog);
        await tcs.Task;
    }

    private void RebuildGroups()
    {
        var groups = Users.GroupBy(u => u.DepartmentId).Select(g =>
        {
            var deptName = g.Key.HasValue ? _departments.FirstOrDefault(d => d.Id == g.Key)?.Name ?? "Неизвестный отдел" : "Без отдела";

            return new DepartmentGroup(deptName, g.Key, new ObservableCollection<UserDTO>(g));
        }).OrderBy(g => g.DepartmentName);

        GroupedUsers = new ObservableCollection<DepartmentGroup>(groups);
        OnPropertyChanged(nameof(FilteredGroups));
    }

    [RelayCommand]
    private async Task ToggleBan(UserDTO user)
    {
        var action = user.IsBanned ? "разблокировать" : "заблокировать";
        var confirmText = user.IsBanned ? "Разблокировать" : "Заблокировать";

        var confirmDialog = new ConfirmDialogViewModel("Подтверждение",
            $"Вы уверены, что хотите {action} пользователя {user.DisplayName ?? user.Username}?",confirmText,"Отмена");

        await _dialogService.ShowAsync(confirmDialog);

        if (!await confirmDialog.Result)
            return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.PostAsync<object>(ApiEndpoints.Admin.ToggleBan(user.Id), null!);

            if (result.Success)
            {
                await LoadAsync();
                SuccessMessage = $"Пользователь {(user.IsBanned ? "разблокирован" : "заблокирован")}";
            }
            else
            {
                ErrorMessage = $"Ошибка: {result.Error}";
            }
        });
    }

    private IEnumerable<DepartmentGroup> ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return GroupedUsers;

        var query = SearchQuery.ToLowerInvariant();

        return GroupedUsers.Select(g => new DepartmentGroup(g.DepartmentName,g.DepartmentId,
            new ObservableCollection<UserDTO>(g.Users.Where(u => MatchesSearch(u, query))))).Where(g => g.Users.Count > 0);
    }

    private static bool MatchesSearch(UserDTO user, string query) =>
        user.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        user.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        user.Surname?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        user.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}