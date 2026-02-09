using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Generic;

public class IndexToTextConverter : ConverterBase<int, string>
{
    public char Separator { get; set; } = '|';

    protected override string? DefaultValue => string.Empty;

    protected override string ConvertCore(int index, object? parameter, CultureInfo culture)
    {
        if (parameter is not string options) return string.Empty;

        var parts = options.Split(Separator);
        return index >= 0 && index < parts.Length ? parts[index] : string.Empty;
    }
}