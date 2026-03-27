using MessengerShared.Dto.Department;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class UserEditDialogViewModel : DialogBaseViewModel
{
    private readonly UserDto? _originalUser;

    #region Properties

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Username { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Surname { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Name { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Midname { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Password { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty] public partial ObservableCollection<DepartmentDto> Departments { get; set; } = [];

    [ObservableProperty] public partial DepartmentDto? SelectedDepartment { get; set; }

    #endregion

    #region Actions

    public Func<CreateUserDto, Task>? CreateAction { get; set; }
    public Func<UserDto, Task>? UpdateAction { get; set; }

    #endregion

    #region Computed Properties

    public bool IsNewUser => _originalUser == null;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Surname) &&
        !string.IsNullOrWhiteSpace(Name) &&
        (!IsNewUser || (!string.IsNullOrWhiteSpace(Password) && Password == ConfirmPassword));

    public string DisplayNamePreview =>
        string.Join(" ", new[] { Surname, Name, Midname }.Where(p => !string.IsNullOrWhiteSpace(p)));

    #endregion

    public UserEditDialogViewModel(UserDto? user, ObservableCollection<DepartmentDto> departments)
    {
        _originalUser = user;
        Departments = departments ?? [];
        Title = user == null ? "Создать сотрудника" : "Редактировать сотрудника";
        CanCloseOnBackgroundClick = true;

        if (user == null) return;

        InitializeFromUser(user);
    }

    #region Initialization

    private void InitializeFromUser(UserDto user)
    {
        Username = user.Username ?? string.Empty;
        Surname = user.Surname ?? string.Empty;
        Name = user.Name ?? string.Empty;
        Midname = user.Midname ?? string.Empty;

        if (user.DepartmentId.HasValue)
        {
            SelectedDepartment = Departments.FirstOrDefault(d => d.Id == user.DepartmentId.Value);
        }
    }

    #endregion

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

    private void ClearErrorIfValid() => ErrorMessage = CanSave ? null : ErrorMessage;

    #endregion

    #region Validation

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Username))
            return "Введите логин";

        if (Username.Length < 3)
            return "Логин должен содержать минимум 3 символа";

        if (string.IsNullOrWhiteSpace(Surname))
            return "Введите фамилию";

        if (string.IsNullOrWhiteSpace(Name))
            return "Введите имя";

        if (IsNewUser)
        {
            if (string.IsNullOrWhiteSpace(Password))
                return "Введите пароль";

            if (Password.Length < 6)
                return "Пароль должен содержать минимум 6 символов";

            if (Password != ConfirmPassword)
                return "Пароли не совпадают";
        }

        return null;
    }

    #endregion

    #region DTO Builders

    private static string TrimLower(string value) => value.Trim().ToLower();

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private CreateUserDto BuildCreateDto() => new()
    {
        Username = TrimLower(Username),
        Password = Password,
        Surname = Surname.Trim(),
        Name = Name.Trim(),
        Midname = TrimOrNull(Midname),
        DepartmentId = SelectedDepartment?.Id
    };

    private UserDto BuildUpdateDto() => new()
    {
        Id = _originalUser!.Id,
        Username = TrimLower(Username),
        Surname = Surname.Trim(),
        Name = Name.Trim(),
        Midname = TrimOrNull(Midname),
        DepartmentId = SelectedDepartment?.Id
    };

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        var validationError = Validate();
        if (validationError != null)
        {
            ErrorMessage = validationError;
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            if (IsNewUser)
                await CreateAction?.Invoke(BuildCreateDto())!;
            else
                await UpdateAction?.Invoke(BuildUpdateDto())!;

            await RequestCloseAsync();
        });
    }
    #endregion
}