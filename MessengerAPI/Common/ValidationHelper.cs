using System.Text.RegularExpressions;

namespace MessengerAPI.Common;

public static partial class ValidationHelper
{
    [GeneratedRegex("^[a-z0-9_]{3,30}$")]
    private static partial Regex UsernamePattern();

    public static Result ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return Result.Failure("Username не может быть пустым");

        if (!UsernamePattern().IsMatch(username))
            return Result.Failure("Username должен содержать 3-30 символов (латинские буквы, цифры, подчёркивания)");
        return Result.Success();
    }

    public static Result ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return Result.Failure("Пароль не может быть пустым");

        if (password.Length < 6)
            return Result.Failure("Пароль должен содержать минимум 6 символов");

        return Result.Success();
    }
}