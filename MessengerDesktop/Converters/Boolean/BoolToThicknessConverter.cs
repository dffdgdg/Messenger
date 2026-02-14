using Avalonia;
using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean;

/// <summary>
/// bool → Thickness.
/// ConverterParameter = "0,1,0,0|0,4,0,0"
/// true → первое значение, false → второе
/// </summary>
public class BoolToThicknessConverter : ConverterBase<bool, Thickness>
{
    public char Separator { get; set; } = '|';
    public Thickness TrueValue { get; set; }
    public Thickness FalseValue { get; set; }

    protected override Thickness DefaultValue => new(0);

    protected override Thickness ConvertCore(bool value, object? parameter, CultureInfo culture)
    {
        if (parameter is string param)
        {
            var parts = param.Split(Separator);
            if (parts.Length == 2)
            {
                var thicknessStr = value ? parts[0] : parts[1];
                return Thickness.Parse(thicknessStr);
            }
        }

        return value ? TrueValue : FalseValue;
    }
}