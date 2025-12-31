using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO.User;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class SelectUserDialogViewModel : DialogBaseViewModel
{
    private readonly TaskCompletionSource<UserDTO?> _tcs = new();

    public SelectUserDialogViewModel(ObservableCollection<UserDTO> users, string title)
    {
        AllUsers = users;
        Title = title;
    }

    public new string Title { get; }

    [ObservableProperty]
    private ObservableCollection<UserDTO> _allUsers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredUsers))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private UserDTO? _selectedUser;

    public ObservableCollection<UserDTO> FilteredUsers
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
                return AllUsers;

            var query = SearchQuery.ToLowerInvariant();
            var filtered = AllUsers.Where(u =>
                u.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                u.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

            return new ObservableCollection<UserDTO>(filtered);
        }
    }

    public bool CanConfirm => SelectedUser != null;

    public Task<UserDTO?> Result => _tcs.Task;

    [RelayCommand]
    private void SelectUser(UserDTO user) => SelectedUser = user;

    [RelayCommand]
    private void Confirm()
    {
        _tcs.TrySetResult(SelectedUser);
        RequestClose();
    }

    partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredUsers));
}