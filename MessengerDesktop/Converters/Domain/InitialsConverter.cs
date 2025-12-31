using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain;

public class InitialsConverter : ConverterBase<string, string>
{
    protected override string? DefaultValue => "?";

    protected override string ConvertCore(string value, object? parameter, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "?";

        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            >= 2 => $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}",
            1 when parts[0].Length >= 2 => $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[0][1])}",
            1 => char.ToUpper(parts[0][0]).ToString(),
            _ => "?"
        };
    }
}