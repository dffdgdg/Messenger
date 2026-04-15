using MessengerDesktop.Infrastructure;
using MessengerShared.Dto.Department;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Dialog;

public partial class UserEditDialogViewModel : DialogBaseViewModel
{
    private readonly UserDto? _originalUser;

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
    public partial ObservableCollection<DepartmentDto> Departments { get; set; } = [];

    [ObservableProperty]
    public partial DepartmentDto? SelectedDepartment { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(PasswordStrength))]
    [NotifyPropertyChangedFor(nameof(PasswordStrengthLabel))]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    public int PasswordStrength => PasswordHelper.CalculateStrength(Password);
    public string PasswordStrengthLabel => PasswordHelper.ToStrengthLabel(PasswordStrength);

    public bool PasswordsMatch =>
        !string.IsNullOrEmpty(Password) && Password == ConfirmPassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(NewPasswordStrength))]
    [NotifyPropertyChangedFor(nameof(NewPasswordStrengthLabel))]
    [NotifyPropertyChangedFor(nameof(NewPasswordSegment1))]
    [NotifyPropertyChangedFor(nameof(NewPasswordSegment2))]
    [NotifyPropertyChangedFor(nameof(NewPasswordSegment3))]
    [NotifyPropertyChangedFor(nameof(NewPasswordSegment4))]
    [NotifyPropertyChangedFor(nameof(NewPasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(IsChangingPassword))]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(NewPasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(IsChangingPassword))]
    public partial string NewConfirmPassword { get; set; } = string.Empty;

    public int NewPasswordStrength => PasswordHelper.CalculateStrength(NewPassword);
    public string NewPasswordStrengthLabel => PasswordHelper.ToStrengthLabel(NewPasswordStrength);

    public bool NewPasswordSegment1 => NewPasswordStrength >= 1;
    public bool NewPasswordSegment2 => NewPasswordStrength >= 2;
    public bool NewPasswordSegment3 => NewPasswordStrength >= 3;
    public bool NewPasswordSegment4 => NewPasswordStrength >= 4;

    public bool NewPasswordsMatch =>
        !string.IsNullOrEmpty(NewPassword) && NewPassword == NewConfirmPassword;

    public bool IsNewUser => _originalUser is null;

    public bool IsChangingPassword =>
        !string.IsNullOrWhiteSpace(NewPassword) || !string.IsNullOrWhiteSpace(NewConfirmPassword);

    public string DisplayNamePreview =>
        string.Join(" ", new[] { Surname, Name, Midname }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public Func<CreateUserDto, Task>? CreateAction { get; set; }
    public Func<UserDto, Task>? UpdateAction { get; set; }
    public Func<string, Task>? ChangePasswordAction { get; set; }

    public UserEditDialogViewModel(UserDto? user, ObservableCollection<DepartmentDto> departments)
    {
        _originalUser = user;
        Departments = departments ?? [];
        Title = user is null ? "Создать сотрудника" : "Редактировать сотрудника";
        CanCloseOnBackgroundClick = true;

        if (user is not null)
            InitializeFromUser(user);
    }

    private void InitializeFromUser(UserDto user)
    {
        Username = user.Username ?? string.Empty;
        Surname = user.Surname ?? string.Empty;
        Name = user.Name ?? string.Empty;
        Midname = user.Midname ?? string.Empty;

        if (user.DepartmentId.HasValue)
            SelectedDepartment = Departments.FirstOrDefault(d => d.Id == user.DepartmentId.Value);
    }

    partial void OnUsernameChanged(string value) => ClearErrorIfValid();
    partial void OnPasswordChanged(string value) => ClearErrorIfValid();
    partial void OnConfirmPasswordChanged(string value) => ClearErrorIfValid();
    partial void OnNewPasswordChanged(string value) => ClearErrorIfValid();
    partial void OnNewConfirmPasswordChanged(string value) => ClearErrorIfValid();

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

    partial void OnMidnameChanged(string value) =>
        OnPropertyChanged(nameof(DisplayNamePreview));

    private void ClearErrorIfValid()
    {
        if (CanSaveExecute()) ErrorMessage = null;
    }

    private string? ValidatePasswordSection()
    {
        if (IsNewUser)
        {
            if (string.IsNullOrWhiteSpace(Password)) return "required";
            if (Password.Length < 6) return "too_short";
            if (Password != ConfirmPassword) return "mismatch";
        }
        else if (IsChangingPassword)
        {
            if (NewPassword.Length > 0 && NewPassword.Length < 6) return "too_short";
            if (NewPassword != NewConfirmPassword) return "mismatch";
        }

        return null;
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Username))
            return "Введите логин";
        if (Username.Trim().Length < 3)
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
        else if (IsChangingPassword)
        {
            if (NewPassword.Length < 6)
                return "Пароль должен содержать минимум 6 символов";
            if (NewPassword != NewConfirmPassword)
                return "Пароли не совпадают";
        }

        return null;
    }
    private bool CanSaveExecute() => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Surname) && !string.IsNullOrWhiteSpace(Name)
        && ValidatePasswordSection() is null;

    private static string TrimLower(string value) => value.Trim().ToLowerInvariant();
    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private CreateUserDto BuildCreateDto() => new()
    {
        Username = TrimLower(Username),
        Password = Password,
        Surname = Surname.Trim(),
        Name = Name.Trim(),
        Midname = TrimOrNull(Midname),
        DepartmentId = SelectedDepartment?.Id,
    };

    private UserDto BuildUpdateDto() => new()
    {
        Id = _originalUser!.Id,
        Username = TrimLower(Username),
        Surname = Surname.Trim(),
        Name = Name.Trim(),
        Midname = TrimOrNull(Midname),
        DepartmentId = SelectedDepartment?.Id,
    };

    [RelayCommand(CanExecute = nameof(CanSaveExecute))]
    private async Task Save()
    {
        var error = Validate();
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            if (IsNewUser)
            {
                await (CreateAction?.Invoke(BuildCreateDto()) ?? Task.CompletedTask);
            }
            else
            {
                await (UpdateAction?.Invoke(BuildUpdateDto()) ?? Task.CompletedTask);

                if (IsChangingPassword && !string.IsNullOrWhiteSpace(NewPassword))
                    await (ChangePasswordAction?.Invoke(NewPassword) ?? Task.CompletedTask);
            }

            await RequestCloseAsync();
        });
    }
}