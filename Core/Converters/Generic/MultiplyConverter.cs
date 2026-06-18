using Core.Converters.Base;
using System.Globalization;

namespace Core.Converters.Generic;

public sealed class MultiplyConverter : ConverterBase<double, double>
{
    protected override double ConvertCore(double value, object? parameter, CultureInfo culture)
    {
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var factor))
        {
            return value * factor;
        }
        return value;
    }
}