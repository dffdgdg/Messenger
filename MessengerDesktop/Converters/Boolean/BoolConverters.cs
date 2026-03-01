using Avalonia.Layout;
using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean;

public abstract class BoolToValueConverter<T> : ConverterBase<bool, T>
{
    public T? TrueValue { get; set; }
    public T? FalseValue { get; set; }

    protected override T? ConvertCore(bool value, object? parameter, CultureInfo culture)
        => value ? TrueValue : FalseValue;
}

public sealed class BoolToStringConverter : BoolToValueConverter<string>
{
    public char Separator { get; set; } = '|';

    protected override string? ConvertCore(bool value, object? parameter, CultureInfo culture)
    {
        if (parameter is string param)
        {
            var parts = param.Split(Separator);
            if (parts.Length == 2)
                return value ? parts[0] : parts[1];
        }
        return base.ConvertCore(value, parameter, culture);
    }
}

public sealed class BoolToDoubleConverter : BoolToValueConverter<double>;

public sealed class BoolToColorConverter : BoolToValueConverter<Color>;

public sealed class BoolToHAlignmentConverter : BoolToValueConverter<HorizontalAlignment>;