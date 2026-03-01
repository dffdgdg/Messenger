using System;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class SelectUserDialogViewModel : DialogBaseViewModel
{
    private readonly TaskCompletionSource<UserDto?> _tcs = new();

    public SelectUserDialogViewModel(ObservableCollection<UserDto> users, string title)
    {
        AllUsers = users;
        Title = title;
    }

    public new string Title { get; }

    [ObservableProperty]
    private ObservableCollection<UserDto> _allUsers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredUsers))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private UserDto? _selectedUser;

    public ObservableCollection<UserDto> FilteredUsers
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
                return AllUsers;

            var query = SearchQuery.ToLowerInvariant();
            var filtered = AllUsers.Where(u =>
                u.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                u.Username?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

            return new ObservableCollection<UserDto>(filtered);
        }
    }

    public bool CanConfirm => SelectedUser != null;

    public Task<UserDto?> Result => _tcs.Task;

    [RelayCommand]
    private void SelectUser(UserDto user) => SelectedUser = user;

    [RelayCommand]
    private void Confirm()
    {
        _tcs.TrySetResult(SelectedUser);
        RequestClose();
    }

    partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredUsers));
}