using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    public partial class AdminViewModel : ViewModelBase
    {
        private readonly HttpClient _httpClient;

        [ObservableProperty]
        private ObservableCollection<UserDTO> users = [];

        [ObservableProperty]
        private ObservableCollection<DepartmentDTO> departments = [];

        [ObservableProperty]
        private ObservableCollection<DepartmentGroup> groupedUsers = [];

        [ObservableProperty]
        private ObservableCollection<HierarchicalDepartmentViewModel> hierarchicalDepartments = [];

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilteredGroupedUsers), nameof(FilteredHierarchicalDepartments))]
        private int selectedTabIndex;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilteredGroupedUsers), nameof(FilteredHierarchicalDepartments))]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DepartmentGroup> filteredGroupedUsers = [];

        [ObservableProperty]
        private ObservableCollection<HierarchicalDepartmentViewModel> filteredHierarchicalDepartments = [];

        [ObservableProperty]
        private bool isUserDialogOpen;

        [ObservableProperty]
        private bool isDepartmentDialogOpen;

        [ObservableProperty]
        private UserEditDialogViewModel? userDialogViewModel;

        [ObservableProperty]
        private DepartmentDialogViewModel? departmentDialogViewModel;

        [ObservableProperty]
        private string? errorMessage;

        public AdminViewModel(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            PropertyChanged += OnPropertyChanged;
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await LoadData();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to load data: " + ex.Message;
            }
        }

        private async Task LoadData()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = string.Empty;

                await LoadDepartments();
                await LoadUsers();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error loading data: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task Refresh()
        {
            await LoadData();
        }

        private async Task LoadUsers()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/admin/users");
                if (response.IsSuccessStatusCode)
                {
                    var loadedUsers = await response.Content.ReadFromJsonAsync<List<UserDTO>>();
                    if (loadedUsers != null)
                    {
                        Users = new ObservableCollection<UserDTO>(loadedUsers);
                        UpdateGroupedUsers();
                    }
                }
                else
                {
                    ErrorMessage = $"Failed to load users. Status code: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error loading users: " + ex.Message;
            }
        }

        private async Task LoadDepartments()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/department");
                if (response.IsSuccessStatusCode)
                {
                    var loadedDepartments = await response.Content.ReadFromJsonAsync<List<DepartmentDTO>>();
                    if (loadedDepartments != null)
                    {
                        Departments = new ObservableCollection<DepartmentDTO>(loadedDepartments);
                        BuildHierarchicalDepartments();
                    }
                }
                else
                {
                    ErrorMessage = $"Failed to load departments. Status code: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error loading departments: " + ex.Message;
            }
        }

        private void UpdateGroupedUsers()
        {
            try
            {
                var grouped = Users
                    .GroupBy(u => u.DepartmentId)
                    .Select(g =>
                    {
                        var dept = g.First().DepartmentId.HasValue
                            ? Departments.FirstOrDefault(d => d.Id == g.First().DepartmentId)?.Name
                            : "No Department";
                        return new DepartmentGroup(
                            dept ?? "No Department",
                            new ObservableCollection<UserDTO>(g)
                        );
                    })
                    .ToList();

                GroupedUsers = new ObservableCollection<DepartmentGroup>(grouped);
                FilteredGroupedUsers = new ObservableCollection<DepartmentGroup>(grouped);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error grouping users: " + ex.Message;
            }
        }

        [RelayCommand]
        private async Task Create()
        {
            if (SelectedTabIndex == 0)
            {
                OpenCreateUserDialog();
            }
            else
            {
                OpenCreateDepartment();
            }
        }

        [RelayCommand]
        private void OpenCreateUserDialog()
        {
            UserDialogViewModel = new UserEditDialogViewModel(null, [.. Departments])
            {
                SaveAction = async (user) =>
                {
                    await CreateUser(user);
                    IsUserDialogOpen = false;
                },
                CancelAction = () => IsUserDialogOpen = false
            };
            IsUserDialogOpen = true;
        }

        [RelayCommand]
        private void OpenCreateDepartment()
        {
            DepartmentDialogViewModel = new DepartmentDialogViewModel([.. Departments])
            {
                SaveAction = async (dept) =>
                {
                    await CreateDepartment(dept);
                    IsDepartmentDialogOpen = false;
                },
                CancelAction = () => IsDepartmentDialogOpen = false
            };
            IsDepartmentDialogOpen = true;
        }

        [RelayCommand]
        private async Task OpenEditUserDialog(UserDTO user)
        {
            try
            {
                UserDialogViewModel = new UserEditDialogViewModel(user, [.. Departments])
                {
                    SaveAction = async (editedUser) =>
                    {
                        await UpdateUser(editedUser);
                        IsUserDialogOpen = false;
                    },
                    CancelAction = () => IsUserDialogOpen = false
                };
                IsUserDialogOpen = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error opening edit dialog: " + ex.Message;
            }
        }

        [RelayCommand]
        private async Task ToggleBan(UserDTO user)
        {
            try
            {
                var response = await _httpClient.PostAsync($"api/user/{user.Id}/toggle-ban", null);
                if (response.IsSuccessStatusCode)
                {
                    await LoadUsers();
                }
                else
                {
                    ErrorMessage = $"Failed to toggle ban. Status code: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error toggling ban: " + ex.Message;
            }
        }

        private async Task CreateUser(UserDTO user)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/user", user);
                if (response.IsSuccessStatusCode)
                {
                    await LoadUsers();
                    NotificationService.ShowSuccess("User created successfully");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ErrorMessage = $"Failed to create user: {error}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error creating user: " + ex.Message;
            }
        }

        private async Task UpdateUser(UserDTO user)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"api/user/{user.Id}", user);
                if (response.IsSuccessStatusCode)
                {
                    await LoadUsers();
                    await NotificationService.ShowSuccess("User updated successfully");
                }
                else
                {
                    ErrorMessage = $"Failed to update user. Status code: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error updating user: " + ex.Message;
            }
        }

        private async Task CreateDepartment(DepartmentDialogViewModel dept)
        {
            try
            {
                var departmentDto = new DepartmentDTO
                {
                    Name = dept.Name,
                    ParentDepartmentId = dept.SelectedParent?.Id
                };

                var response = await _httpClient.PostAsJsonAsync("api/department", departmentDto);
                if (response.IsSuccessStatusCode)
                {
                    await LoadDepartments();
                    await NotificationService.ShowSuccess("Department created successfully");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ErrorMessage = $"Failed to create department: {error}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error creating department: " + ex.Message;
            }
        }

        private void BuildHierarchicalDepartments()
        {
            try
            {
                var rootDepartments = Departments
                    .Where(d => !d.ParentDepartmentId.HasValue)
                    .Select(d => CreateHierarchicalDepartment(d, 0))
                    .ToList();

                HierarchicalDepartments = new ObservableCollection<HierarchicalDepartmentViewModel>(rootDepartments);
                FilteredHierarchicalDepartments = new ObservableCollection<HierarchicalDepartmentViewModel>(rootDepartments);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error building department hierarchy: " + ex.Message;
            }
        }

        private HierarchicalDepartmentViewModel CreateHierarchicalDepartment(DepartmentDTO department, int level)
        {
            var viewModel = new HierarchicalDepartmentViewModel(department, level);
            var children = Departments
                .Where(d => d.ParentDepartmentId == department.Id)
                .Select(d => CreateHierarchicalDepartment(d, level + 1));
            foreach (var child in children)
            {
                viewModel.Children.Add(child);
            }
            return viewModel;
        }

        private void FilterUsers()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                FilteredGroupedUsers = new ObservableCollection<DepartmentGroup>(GroupedUsers);
                return;
            }

            var query = SearchQuery.ToLower();
            var filtered = GroupedUsers
                .Select(group => new DepartmentGroup(
                    group.DepartmentName,
                    new ObservableCollection<UserDTO>(
                        group.Users.Where(user =>
                            (user.DisplayName?.ToLower(System.Globalization.CultureInfo.CurrentCulture).Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                            (user.Username?.ToLower(System.Globalization.CultureInfo.CurrentCulture).Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                        )
                    )
                ))
                .Where(group => group.Users.Any())
                .ToList();

            FilteredGroupedUsers = new ObservableCollection<DepartmentGroup>(filtered);
        }

        private void FilterDepartments()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                FilteredHierarchicalDepartments = new ObservableCollection<HierarchicalDepartmentViewModel>(HierarchicalDepartments);
                return;
            }

            var query = SearchQuery.ToLower();
            var filteredDepartments = new ObservableCollection<HierarchicalDepartmentViewModel>();

            foreach (var department in HierarchicalDepartments)
            {
                var clone = CloneAndFilterDepartment(department, query);
                if (clone != null)
                {
                    filteredDepartments.Add(clone);
                }
            }

            FilteredHierarchicalDepartments = filteredDepartments;
        }

        private static HierarchicalDepartmentViewModel? CloneAndFilterDepartment(HierarchicalDepartmentViewModel original, string searchQuery)
        {
            // Check if this department or any of its children match the search
            bool matchesSearch = original.Name.Contains(searchQuery, StringComparison.CurrentCultureIgnoreCase);
            var matchingChildren = new List<HierarchicalDepartmentViewModel>();

            // Recursively check children
            foreach (var child in original.Children)
            {
                var matchingChild = CloneAndFilterDepartment(child, searchQuery);
                if (matchingChild != null)
                {
                    matchingChildren.Add(matchingChild);
                    matchesSearch = true; // If any child matches, we want to show this parent
                }
            }

            // If this department or any of its children match, create a filtered clone
            if (matchesSearch)
            {
                var clone = new HierarchicalDepartmentViewModel(new DepartmentDTO
                {
                    Id = original.Id,
                    Name = original.Name,
                    ParentDepartmentId = original.ParentDepartmentId
                }, original.Level)
                {
                    IsExpanded = true // Always expand when filtering
                };

                // Add matching children
                foreach (var child in matchingChildren)
                {
                    clone.Children.Add(child);
                }

                return clone;
            }

            return null;
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SearchQuery))
            {
                if (SelectedTabIndex == 0)
                {
                    FilterUsers();
                }
                else
                {
                    FilterDepartments();
                }
            }
            else if (e.PropertyName == nameof(SelectedTabIndex))
            {
                UpdateSearch();
            }
        }

        private void UpdateSearch()
        {
            if (!string.IsNullOrEmpty(SearchQuery))
            {
                if (SelectedTabIndex == 0)
                {
                    FilterUsers();
                }
                else
                {
                    FilterDepartments();
                }
            }
        }

        [RelayCommand]
        private void OpenEditDepartment(DepartmentDTO department)
        {
            try
            {
                DepartmentDialogViewModel = new DepartmentDialogViewModel([.. Departments], department)
                {
                    SaveAction = async (dept) =>
                    {
                        await UpdateDepartment(dept, department.Id);
                        IsDepartmentDialogOpen = false;
                    },
                    CancelAction = () => IsDepartmentDialogOpen = false
                };
                IsDepartmentDialogOpen = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error opening department edit dialog: " + ex.Message;
            }
        }

        private async Task UpdateDepartment(DepartmentDialogViewModel dept, int departmentId)
        {
            try
            {
                var departmentDto = new DepartmentDTO
                {
                    Id = departmentId,
                    Name = dept.Name,
                    ParentDepartmentId = dept.SelectedParent?.Id
                };

                var response = await _httpClient.PutAsJsonAsync($"api/department/{departmentId}", departmentDto);
                if (response.IsSuccessStatusCode)
                {
                    await LoadDepartments();
                    await NotificationService.ShowSuccess("Department updated successfully");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ErrorMessage = $"Failed to update department: {error}";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error updating department: " + ex.Message;
            }
        }
    }

    public class DepartmentGroup(string departmentName, ObservableCollection<UserDTO> users)
    {
        public string DepartmentName { get; } = departmentName;
        public ObservableCollection<UserDTO> Users { get; } = users;
    }
}
