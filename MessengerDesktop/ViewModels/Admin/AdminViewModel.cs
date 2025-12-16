using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.Services.Api;
using MessengerShared.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels
{
    /// <summary>
    /// ViewModel админ-панели для управления пользователями и отделами.
    /// </summary>
    public partial class AdminViewModel : BaseViewModel
    {
        private readonly IApiClientService _apiClient;
        private readonly MainWindowViewModel _mainWindowViewModel;

        [ObservableProperty]
        private ObservableCollection<UserDTO> users = [];

        [ObservableProperty]
        private ObservableCollection<DepartmentDTO> departments = [];

        [ObservableProperty]
        private ObservableCollection<DepartmentGroup> groupedUsers = [];

        [ObservableProperty]
        private ObservableCollection<HierarchicalDepartmentViewModel> hierarchicalDepartments = [];

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

        public AdminViewModel(IApiClientService apiClient, MainWindowViewModel mainWindowViewModel)
        {
            _apiClient = apiClient;
            _mainWindowViewModel = mainWindowViewModel;
            PropertyChanged += OnPropertyChanged;
            InitializeAsync();
        }

        private async void InitializeAsync() =>
            await SafeExecuteAsync(LoadData);

        private async Task LoadData() 
            => await SafeExecuteAsync(async () => { await LoadDepartments(); await LoadUsers(); });

        [RelayCommand]
        private async Task Refresh() 
            => await LoadData();

        private async Task LoadUsers()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== LoadUsers START ===");

                var result = await _apiClient.GetAsync<List<UserDTO>>("api/admin/users");

                System.Diagnostics.Debug.WriteLine($"Result - Success: {result.Success}");
                System.Diagnostics.Debug.WriteLine($"Result - Error: '{result.Error}'");
                System.Diagnostics.Debug.WriteLine($"Result - Message: '{result.Message}'");
                System.Diagnostics.Debug.WriteLine($"Result - Data: {result.Data}");
                System.Diagnostics.Debug.WriteLine($"Result - Data is null: {result.Data == null}");
                System.Diagnostics.Debug.WriteLine($"Result - Data count: {result.Data?.Count ?? 0}");

                if (result.Data != null)
                {
                    foreach (var user in result.Data.Take(3))
                    {
                        System.Diagnostics.Debug.WriteLine($"User {user.Id}: {user.Username}, Theme: {user.Theme}");
                    }
                }

                if (result.Success && result.Data != null)
                {
                    Users = new ObservableCollection<UserDTO>(result.Data);
                    UpdateGroupedUsers();
                    System.Diagnostics.Debug.WriteLine("LoadUsers - SUCCESS");
                }
                else
                {
                    ErrorMessage = $"Failed to load users: {result.Error}";
                    System.Diagnostics.Debug.WriteLine($"LoadUsers - FAILED: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadUsers - EXCEPTION: {ex}");
                ErrorMessage = $"Exception loading users: {ex.Message}";
            }

            System.Diagnostics.Debug.WriteLine("=== LoadUsers END ===");
        }

        private async Task LoadDepartments()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== LoadDepartments START ===");

                var result = await _apiClient.GetAsync<List<DepartmentDTO>>("api/department");

                System.Diagnostics.Debug.WriteLine($"LoadDepartments - Success: {result.Success}");
                System.Diagnostics.Debug.WriteLine($"LoadDepartments - Error: {result.Error}");
                System.Diagnostics.Debug.WriteLine($"LoadDepartments - Data: {result.Data != null}");
                System.Diagnostics.Debug.WriteLine($"LoadDepartments - Data count: {result.Data?.Count ?? 0}");

                if (result.Success && result.Data != null)
                {
                    Departments = new ObservableCollection<DepartmentDTO>(result.Data);
                    BuildHierarchicalDepartments();
                    System.Diagnostics.Debug.WriteLine("LoadDepartments - SUCCESS");
                }
                else
                {
                    ErrorMessage = $"Failed to load departments: {result.Error}";
                    System.Diagnostics.Debug.WriteLine($"LoadDepartments - FAILED: {result.Error}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadDepartments EXCEPTION: {ex}");
                ErrorMessage = $"Exception loading departments: {ex.Message}";
            }

            System.Diagnostics.Debug.WriteLine("=== LoadDepartments END ===");
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
                await OpenCreateUserDialog();            
            else 
                await OpenCreateDepartment();
        }

        [RelayCommand]
        private async Task OpenCreateUserDialog()
        {
            var dialog = new UserEditDialogViewModel(null, Departments)
            {
                SaveAction = async (user) => await CreateUser(user)
            };
            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }

        [RelayCommand]
        private async Task OpenCreateDepartment()
        {
            var dialog = new DepartmentDialogViewModel([.. Departments])
            {
                SaveAction = async (dept) => await CreateDepartment(dept)
            };
            await _mainWindowViewModel.ShowDialogAsync(dialog);
        }

        [RelayCommand]
        private async Task OpenEditUserDialog(UserDTO user)
        {
            await SafeExecuteAsync(async () =>
            {
                var dialog = new UserEditDialogViewModel(user, Departments)
                {
                    SaveAction = async (editedUser) => await UpdateUser(editedUser)
                };
                await _mainWindowViewModel.ShowDialogAsync(dialog);
            });
        }

        [RelayCommand]
        private async Task ToggleBan(UserDTO user)
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.PostAsync($"api/admin/users/{user.Id}/toggle-ban", null);
                if (result.Success)
                {
                    await LoadUsers();
                    SuccessMessage = "User ban status updated";
                }
                else
                {
                    ErrorMessage = $"Failed to toggle ban: {result.Error}";
                }
            });
        }

        private async Task CreateUser(UserDTO user)
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.PostAsync<UserDTO>("api/admin/users", user);
                if (result.Success)
                {
                    await LoadUsers();
                    SuccessMessage = "User created successfully";
                }
                else
                {
                    ErrorMessage = $"Failed to create user: {result.Error}";
                }
            });
        }

        private async Task UpdateUser(UserDTO user)
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.PutAsync<UserDTO>($"api/user/{user.Id}", user);
                if (result.Success)
                {
                    await LoadUsers();
                    SuccessMessage = "User updated successfully";
                }
                else
                {
                    ErrorMessage = $"Failed to update user: {result.Error}";
                }
            });
        }

        private async Task CreateDepartment(DepartmentDialogViewModel dept)
        {
            await SafeExecuteAsync(async () =>
            {
                var departmentDto = new DepartmentDTO
                {
                    Name = dept.Name,
                    ParentDepartmentId = dept.SelectedParent?.Id > 0 ? dept.SelectedParent.Id : null
                };

                var result = await _apiClient.PostAsync<DepartmentDTO>("api/department", departmentDto);
                if (result.Success)
                {
                    await LoadDepartments();
                    SuccessMessage = "Department created successfully";
                }
                else
                {
                    ErrorMessage = $"Failed to create department: {result.Error}";
                }
            });
        }

        private void BuildHierarchicalDepartments()
        {
            try
            {
                var rootDepartments = Departments.Where(d => !d.ParentDepartmentId.HasValue)
                    .Select(d => CreateHierarchicalDepartment(d, 0)).ToList();

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
            var children = Departments.Where(d => d.ParentDepartmentId == department.Id).Select(d => CreateHierarchicalDepartment(d, level + 1));
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
            var filtered = GroupedUsers.Select(group => new DepartmentGroup(
                    group.DepartmentName,
                    new ObservableCollection<UserDTO>(
                        group.Users.Where(user =>
                            (user.DisplayName?.ToLower().Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                            (user.Username?.ToLower().Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                        )
                    )
                ))
                .Where(group => group.Users.Any()).ToList();

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
            bool matchesSearch = original.Name.Contains(searchQuery, StringComparison.CurrentCultureIgnoreCase);
            var matchingChildren = new List<HierarchicalDepartmentViewModel>();

            foreach (var child in original.Children)
            {
                var matchingChild = CloneAndFilterDepartment(child, searchQuery);
                if (matchingChild != null)
                {
                    matchingChildren.Add(matchingChild);
                    matchesSearch = true;
                }
            }

            if (matchesSearch)
            {
                var clone = new HierarchicalDepartmentViewModel(new DepartmentDTO
                {
                    Id = original.Id,
                    Name = original.Name,
                    ParentDepartmentId = original.ParentDepartmentId
                }, original.Level)
                {
                    IsExpanded = true
                };

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
                if (SelectedTabIndex == 0) FilterUsers();
                else FilterDepartments();
            }
            else if (e.PropertyName == nameof(SelectedTabIndex)) 
                UpdateSearch();
        }

        private void UpdateSearch()
        {
            if (!string.IsNullOrEmpty(SearchQuery))
            {
                if (SelectedTabIndex == 0)
                    FilterUsers();
                else
                    FilterDepartments();
            }
        }

        public int UsersCount => Users?.Count ?? 0;
        public int DepartmentsCount => Departments?.Count ?? 0;

        [RelayCommand]
        private async Task OpenEditDepartment(DepartmentDTO department)
        {
            try
            {
                var dialog = new DepartmentDialogViewModel([.. Departments], department)
                {
                    SaveAction = async (dept) => await UpdateDepartment(dept, department.Id)
                };
                await _mainWindowViewModel.ShowDialogAsync(dialog);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error opening department edit dialog: " + ex.Message;
            }
        }

        [RelayCommand]
        private void ClearError() 
            => ErrorMessage = string.Empty;

        [RelayCommand]
        private void ClearSuccess() 
            => SuccessMessage = string.Empty;

        [RelayCommand]
        private async Task DeleteDepartment(DepartmentDTO department)
        {
            await SafeExecuteAsync(async () =>
            {
                var result = await _apiClient.DeleteAsync($"api/department/{department.Id}");
                if (result.Success)
                {
                    await LoadDepartments();
                    SuccessMessage = "Отдел удалён";
                }
                else
                {
                    ErrorMessage = $"Ошибка удаления: {result.Error}";
                }
            });
        }
        private async Task UpdateDepartment(DepartmentDialogViewModel dept, int departmentId)
        {
            await SafeExecuteAsync(async () =>
            {
                var departmentDto = new DepartmentDTO
                {
                    Id = departmentId,
                    Name = dept.Name,
                    ParentDepartmentId = dept.SelectedParent?.Id > 0 ? dept.SelectedParent.Id : null
                };

                var result = await _apiClient.PutAsync<DepartmentDTO>($"api/department/{departmentId}", departmentDto);
                if (result.Success)
                {
                    await LoadDepartments();
                    SuccessMessage = "Department updated successfully";
                }
                else
                {
                    ErrorMessage = $"Failed to update department: {result.Error}";
                }
            });
        }
    }

    /// <summary>Группа пользователей по отделам</summary>
    public class DepartmentGroup(string departmentName, ObservableCollection<UserDTO> users)
    {
        public string DepartmentName { get; } = departmentName;
        public ObservableCollection<UserDTO> Users { get; } = users;
        public bool IsBanned { get; set; }
    }
}