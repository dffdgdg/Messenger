using Avalonia.Data.Converters;
using MessengerShared.Enum;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters;

public class ThemeToDisplayConverter : IValueConverter
{
    public static readonly ThemeToDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            Theme.dark => "Тёмная",
            Theme.light => "Светлая",
            Theme.system => "Системная",
            _ => value?.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}