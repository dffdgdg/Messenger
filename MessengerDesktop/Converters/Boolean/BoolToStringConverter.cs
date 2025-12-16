using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean;

public class BoolToStringConverter : ValueConverterBase<bool, string>
{
    public string TrueValue { get; set; } = "True";
    public string FalseValue { get; set; } = "False";
    public char ParameterSeparator { get; set; } = '|';

    protected override string ConvertCore(bool value, object? parameter, CultureInfo culture)
    {
        if (parameter is string param)
        {
            var parts = param.Split(ParameterSeparator);
            if (parts.Length == 2)
                return value ? parts[0] : parts[1];
        }

        return value ? TrueValue : FalseValue;
    }

    protected override object? HandleInvalidInput(object? value) => string.Empty;
}