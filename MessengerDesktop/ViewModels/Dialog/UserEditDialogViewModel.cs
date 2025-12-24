using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessengerDesktop.ViewModels.Dialog;
using MessengerShared.DTO;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels;

public partial class UserEditDialogViewModel : DialogBaseViewModel
{
    private readonly UserDTO? _originalUser;

    #region Основные данные

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _surname = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _midname = string.Empty;

    #endregion

    #region Пароль (только для нового пользователя)

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _confirmPassword = string.Empty;

    #endregion

    #region Отдел

    [ObservableProperty]
    private ObservableCollection<DepartmentDTO> _departments = [];

    [ObservableProperty]
    private DepartmentDTO? _selectedDepartment;

    #endregion

    #region Состояние и Actions

    /// <summary>
    /// Action для создания нового пользователя
    /// </summary>
    public Func<CreateUserDTO, Task>? CreateAction { get; set; }

    /// <summary>
    /// Action для обновления существующего пользователя
    /// </summary>
    public Func<UserDTO, Task>? UpdateAction { get; set; }

    public bool IsNewUser => _originalUser == null;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Surname) &&
        !string.IsNullOrWhiteSpace(Name) &&
        (!IsNewUser || (!string.IsNullOrWhiteSpace(Password) && Password == ConfirmPassword));

    /// <summary>
    /// Предпросмотр отображаемого имени
    /// </summary>
    public string DisplayNamePreview
    {
        get
        {
            var parts = new[] { Surname, Name, Midname }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            return string.Join(" ", parts);
        }
    }

    #endregion

    public UserEditDialogViewModel(UserDTO? user, ObservableCollection<DepartmentDTO> departments)
    {
        _originalUser = user;
        Departments = departments ?? [];

        Title = user == null ? "Создать сотрудника" : "Редактировать сотрудника";
        CanCloseOnBackgroundClick = true;

        if (user == null) return;

        Username = user.Username ?? string.Empty;
        Surname = user.Surname ?? string.Empty;
        Name = user.Name ?? string.Empty;
        Midname = user.Midname ?? string.Empty;

        // Находим отдел по ID
        if (user.DepartmentId.HasValue)
        {
            SelectedDepartment = Departments.FirstOrDefault(d => d.Id == user.DepartmentId.Value);
        }
    }

    #region Property Changed Handlers

    partial void OnUsernameChanged(string value) => ClearErrorIfValid();

    partial void OnSurnameChanged(string value)
    {
        ClearErrorIfValid();
        OnPropertyChanged(nameof(DisplayNamePreview));
    }

    partial void OnNameChanged(string value)
    {
        ClearErrorIfValid();
        OnPropertyChanged(nameof(DisplayNamePreview));
    }

    partial void OnMidnameChanged(string value) => OnPropertyChanged(nameof(DisplayNamePreview));

    partial void OnPasswordChanged(string value) => ClearErrorIfValid();

    partial void OnConfirmPasswordChanged(string value) => ClearErrorIfValid();

    private void ClearErrorIfValid()
    {
        if (CanSave) ErrorMessage = null;
    }

    #endregion

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        // Валидация
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Введите логин";
            return;
        }

        if (Username.Length < 3)
        {
            ErrorMessage = "Логин должен содержать минимум 3 символа";
            return;
        }

        if (string.IsNullOrWhiteSpace(Surname))
        {
            ErrorMessage = "Введите фамилию";
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Введите имя";
            return;
        }

        if (IsNewUser)
        {
            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Введите пароль";
                return;
            }

            if (Password.Length < 6)
            {
                ErrorMessage = "Пароль должен содержать минимум 6 символов";
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Пароли не совпадают";
                return;
            }
        }

        await SafeExecuteAsync(async () =>
        {
            if (IsNewUser)
            {
                if (CreateAction == null) return;

                var createDto = new CreateUserDTO
                {
                    Username = Username.Trim().ToLower(),
                    Password = Password,
                    Surname = Surname.Trim(),
                    Name = Name.Trim(),
                    Midname = string.IsNullOrWhiteSpace(Midname) ? null : Midname.Trim(),
                    DepartmentId = SelectedDepartment?.Id
                };

                await CreateAction(createDto);
            }
            else
            {
                if (UpdateAction == null) return;

                var updateDto = new UserDTO
                {
                    Id = _originalUser!.Id,
                    Username = Username.Trim().ToLower(),
                    Surname = Surname.Trim(),
                    Name = Name.Trim(),
                    Midname = string.IsNullOrWhiteSpace(Midname) ? null : Midname.Trim(),
                    DepartmentId = SelectedDepartment?.Id
                };

                await UpdateAction(updateDto);
            }

            RequestClose();
        });
    }
}