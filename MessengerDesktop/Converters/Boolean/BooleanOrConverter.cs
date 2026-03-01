using System;
using System.Collections.Generic;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean;

public sealed class BooleanOrConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is bool boolValue && boolValue)
                return true;
        }
        return false;
    }
}