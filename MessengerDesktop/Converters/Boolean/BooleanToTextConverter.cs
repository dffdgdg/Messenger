using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean
{
    public class BooleanToTextConverter : IValueConverter
    {
        object? IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not bool booleanValue)
                return string.Empty;

            if (parameter is not string param)
                return string.Empty;

            var parts = param.Split(';');
            if (parts.Length != 2)
                return string.Empty;

            return booleanValue ? parts[0] : parts[1];
        }

        object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
