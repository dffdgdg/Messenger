using CommunityToolkit.Mvvm.ComponentModel;
using Core.Infrastructure.Helpers;
using Shared.Dto.Department;

namespace Core.ViewModels.Dialog;

public partial class UserEditDialogViewModel : DialogBaseViewModel
{
    private const int MinUsernameLength = 3;
    private const int MinPasswordLength = 6;

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
    public bool PasswordsMatch => IsNonEmptyAndEqual(Password, ConfirmPassword);

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

    public bool NewPasswordsMatch => IsNonEmptyAndEqual(NewPassword, NewConfirmPassword);

    public bool IsNewUser => _originalUser is null;

    public bool IsChangingPassword => !string.IsNullOrWhiteSpace(NewPassword) || !string.IsNullOrWhiteSpace(NewConfirmPassword);

    public string DisplayNamePreview => string.Join(" ", new[] { Surname, Name, Midname }.Where(p => !string.IsNullOrWhiteSpace(p)));

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

    partial void OnMidnameChanged(string value) => OnPropertyChanged(nameof(DisplayNamePreview));

    private void ClearErrorIfValid()
    {
        if (CanSaveExecute())
            ErrorMessage = null;
    }

    private bool CanSaveExecute() => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Surname) && !string.IsNullOrWhiteSpace(Name) &&
        ValidatePasswordSection() is null;

    private string? ValidatePasswordSection() => IsNewUser ? ValidateNewUserPassword() : ValidatePasswordChange();

    private string? ValidateNewUserPassword()
    {
        if (string.IsNullOrWhiteSpace(Password)) return "required";
        if (Password.Length < MinPasswordLength) return "too_short";
        if (Password != ConfirmPassword) return "mismatch";
        return null;
    }

    private string? ValidatePasswordChange()
    {
        if (!IsChangingPassword) return null;
        if (NewPassword.Length > 0 && NewPassword.Length < MinPasswordLength) return "too_short";
        if (NewPassword != NewConfirmPassword) return "mismatch";
        return null;
    }

    private string? Validate() => ValidateIdentityFields() ?? (IsNewUser ? ValidateNewUserPasswordFull() : ValidatePasswordChangeFull());

    private string? ValidateIdentityFields()
    {
        if (string.IsNullOrWhiteSpace(Username))
            return "Введите логин";
        if (Username.Trim().Length < MinUsernameLength)
            return "Логин должен содержать минимум 3 символа";
        if (string.IsNullOrWhiteSpace(Surname))
            return "Введите фамилию";
        if (string.IsNullOrWhiteSpace(Name))
            return "Введите имя";

        return null;
    }

    private string? ValidateNewUserPasswordFull()
    {
        if (string.IsNullOrWhiteSpace(Password))
            return "Введите пароль";
        if (Password.Length < MinPasswordLength)
            return "Пароль должен содержать минимум 6 символов";
        if (Password != ConfirmPassword)
            return "Пароли не совпадают";

        return null;
    }

    private string? ValidatePasswordChangeFull()
    {
        if (!IsChangingPassword) return null;
        if (NewPassword.Length < MinPasswordLength)
            return "Пароль должен содержать минимум 6 символов";
        if (NewPassword != NewConfirmPassword)
            return "Пароли не совпадают";

        return null;
    }

    private static string TrimLower(string value) => value.Trim().ToLowerInvariant();
    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
                await CreateUserAsync();
            else
                await UpdateUserAsync();

            await RequestCloseAsync();
        });
    }

    private async Task CreateUserAsync() => await (CreateAction?.Invoke(BuildCreateDto()) ?? Task.CompletedTask);

    private async Task UpdateUserAsync()
    {
        await (UpdateAction?.Invoke(BuildUpdateDto()) ?? Task.CompletedTask);

        if (IsChangingPassword && !string.IsNullOrWhiteSpace(NewPassword))
            await (ChangePasswordAction?.Invoke(NewPassword) ?? Task.CompletedTask);
    }

    private static bool IsNonEmptyAndEqual(string a, string b) => !string.IsNullOrEmpty(a) && a == b;
}