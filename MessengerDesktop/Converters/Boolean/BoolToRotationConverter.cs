using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean;

public class BoolToRotationConverter : ValueConverterBase<bool, double>
{
    public double TrueValue { get; set; } = 90;
    public double FalseValue { get; set; } = 0;

    protected override double ConvertCore(bool value, object? parameter, CultureInfo culture)
        => value ? TrueValue : FalseValue;
}