using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Comparsion;

public class EqualityConverter : ValueConverterBase
{
    public bool Invert { get; set; }

    protected override object? ConvertCore(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var areEqual = value?.ToString()?.Equals(parameter?.ToString(), StringComparison.OrdinalIgnoreCase) ?? (parameter == null);

        return Invert ? !areEqual : areEqual;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) 
        => throw new NotSupportedException("EqualityToBoolConverter does not support ConvertBack");
}

public class NotEqualityConverter : EqualityConverter
{
    public NotEqualityConverter() => Invert = true;
}