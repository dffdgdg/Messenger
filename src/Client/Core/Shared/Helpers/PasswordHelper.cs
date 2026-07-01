namespace Core.Shared.Helpers;

public static class PasswordHelper
{
    public static int CalculateStrength(string? password)
    {
        if (string.IsNullOrEmpty(password)) return 0;

        int score = 0;
        if (password.Length >= 6) score++;
        if (password.Length >= 10) score++;
        if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
        if (password.Any(char.IsDigit) || password.Any(c => !char.IsLetterOrDigit(c))) score++;

        return score;
    }

    public static string ToStrengthLabel(int strength) => strength switch
    {
        0 => string.Empty,
        1 => "Очень слабый",
        2 => "Слабый",
        3 => "Хороший",
        _ => "Надёжный",
    };
}