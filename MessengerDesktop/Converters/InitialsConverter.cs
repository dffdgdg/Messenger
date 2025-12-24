using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class InitialsConverter : ValueConverterBase<string, string>
    {
        public static readonly InitialsConverter Instance = new();

        protected override string? ConvertCore(string value, object? parameter, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "?";

            var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";

            if (parts.Length == 1 && parts[0].Length >= 2)
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[0][1])}";

            if (parts.Length == 1)
                return char.ToUpper(parts[0][0]).ToString();

            return "?";
        }

        protected override object? HandleError(Exception ex, object? value) => "?";

        protected override object? HandleInvalidInput(object? value) => "?";
    }
}