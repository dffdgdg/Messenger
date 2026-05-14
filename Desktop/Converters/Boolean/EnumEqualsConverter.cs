using Desktop.Converters.Base;
using System.Globalization;

namespace Desktop.Converters.Boolean;

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

public sealed class EnumNotEqualsConverter : ConverterBase
{
    protected override object? DefaultValue => true;

    protected override object? ConvertCore(object? value, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return true;

        return !string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}