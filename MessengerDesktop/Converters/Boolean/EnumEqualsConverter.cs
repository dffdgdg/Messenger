using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean;

/// <summary>
/// Сравнивает значение enum со строковым параметром.
/// ConverterParameter = "First" → true если value.ToString() == "First"
/// </summary>
public sealed class EnumEqualsConverter : ConverterBase
{
    protected override object? DefaultValue => false;

    protected override object? ConvertCore(object? value, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}