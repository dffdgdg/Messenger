using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters;

public class BoolToOnlineColorConverter : IValueConverter
{
    public static readonly BoolToOnlineColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Color.Parse("#22C55E") : Color.Parse("#6B7280");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}