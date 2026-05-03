using Desktop.Services.Core.Api;
using Desktop.ViewModels.Admin;
using Desktop.ViewModels.Dialog;
using Shared.Dto.Department;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Desktop.ViewModels;

public partial class UsersTabViewModel(IApiClientService apiClient, IDialogService dialogService) : BaseViewModel
{
    private readonly IApiClientService _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    private readonly IDialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

    private IReadOnlyList<DepartmentDto> _departments = [];

    [ObservableProperty]
    public partial ObservableCollection<UserDto> Users { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DepartmentGroup> GroupedUsers { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGroups))]
    public partial string SearchQuery { get; set; } = string.Empty;

    public IEnumerable<DepartmentGroup> FilteredGroups => ApplyFilter();

    public void SetDepartments(IReadOnlyList<DepartmentDto> departments)
    {
        _departments = departments;
        RebuildGroups();
    }

    public async Task LoadAsync() => await SafeExecuteAsync(async () =>
    {
        var result = await _apiClient.GetAsync<List<UserDto>>(ApiEndpoints.Admin.AllUsers);

        if (result is { Success: true, Data: not null })
        {
            Users = new ObservableCollection<UserDto>(result.Data);
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
        var dialog = new UserEditDialogViewModel(null, new ObservableCollection<DepartmentDto>(_departments));

        var tcs = new TaskCompletionSource<bool>();

        dialog.CreateAction = async createDto =>
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.PostAsync<UserDto>(ApiEndpoints.Admin.AllUsers, createDto);

                if (result.Success)
                {
                    tcs.TrySetResult(true);
                    SuccessMessage = "Сотрудник создан";
                }
                else
                {
                    dialog.ErrorMessage = $"Ошибка создания: {result.Error}";
                    tcs.TrySetResult(false);
                }
            });
        };

        dialog.CloseRequested += () =>
        {
            tcs.TrySetResult(false);
            return Task.CompletedTask;
        };

        await _dialogService.ShowAsync(dialog);
        await tcs.Task;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task Edit(UserDto user)
    {
        var dialog = new UserEditDialogViewModel(user, new ObservableCollection<DepartmentDto>(_departments));

        var tcs = new TaskCompletionSource<bool>();

        dialog.UpdateAction = async updateDto =>
        {
            var result = await _apiClient.PutAsync<UserDto>(ApiEndpoints.Admin.UserById(user.Id), updateDto);

            if (result.Success)
            {
                SuccessMessage = "Профиль сотрудника обновлён";
            }
            else
            {
                dialog.ErrorMessage = $"Ошибка обновления: {result.Error}";
                throw new InvalidOperationException(result.Error);
            }
        };

        dialog.ChangePasswordAction = async newPassword =>
        {
            var result = await _apiClient.PostAsync<object>(ApiEndpoints.Admin.ResetPassword(user.Id), new ResetPasswordAdminDto { NewPassword = newPassword });

            if (result.Success)
            {
                SuccessMessage = "Профиль и пароль сотрудника обновлены";
            }
            else
            {
                dialog.ErrorMessage = $"Профиль сохранён, но пароль не изменён: {result.Error}";
                throw new InvalidOperationException(result.Error);
            }
        };

        dialog.CloseRequested += () =>
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        };

        await _dialogService.ShowAsync(dialog);
        await tcs.Task;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleBan(UserDto user)
    {
        var action = user.IsBanned ? "разблокировать" : "заблокировать";
        var actionLabel = user.IsBanned ? "Разблокировать" : "Заблокировать";
        var userName = user.DisplayName ?? user.Username;

        var confirmDialog = new ConfirmDialogViewModel("Подтверждение", $"Вы уверены, что хотите {action} сотрудника {userName}?", actionLabel, "Отмена");

        await _dialogService.ShowAsync(confirmDialog);

        if (!await confirmDialog.Result)
            return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _apiClient.PostAsync<object>(ApiEndpoints.Admin.ToggleBan(user.Id), null!);

            if (result.Success)
            {
                await LoadAsync();
                SuccessMessage = user.IsBanned ? $"Сотрудник {userName} разблокирован" : $"Сотрудник {userName} заблокирован";
            }
            else
            {
                ErrorMessage = $"Ошибка: {result.Error}";
            }
        });
    }

    private void RebuildGroups()
    {
        var groups = Users.GroupBy(u => u.DepartmentId).Select(g => new DepartmentGroup(g.Key.HasValue
            ? _departments.FirstOrDefault(d => d.Id == g.Key)?.Name ?? "Неизвестный отдел" : "Без отдела", g.Key,
            new ObservableCollection<UserDto>(g))).OrderBy(g => g.DepartmentId.HasValue ? 0 : 1).ThenBy(g => g.DepartmentName);

        GroupedUsers = new ObservableCollection<DepartmentGroup>(groups);
        OnPropertyChanged(nameof(FilteredGroups));
    }

    private IEnumerable<DepartmentGroup> ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return GroupedUsers;

        var query = SearchQuery.ToLowerInvariant();

        return GroupedUsers.Select(g => new DepartmentGroup(g.DepartmentName, g.DepartmentId,
            new ObservableCollection<UserDto>(g.Users.Where(u => MatchesSearch(u, query))))).Where(g => g.Users.Count > 0);
    }

    private static bool MatchesSearch(UserDto user, string query) =>
        user.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        user.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        user.Surname?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        user.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}