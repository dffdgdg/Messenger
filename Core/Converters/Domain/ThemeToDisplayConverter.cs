using Core.Converters.Base;
using System.Globalization;

namespace Core.Converters.Domain;

public class ThemeToDisplayConverter : ConverterBase<Theme, string>
{
    protected override bool AllowNull => true;

    protected override string? ConvertCore(Theme value, object? parameter, CultureInfo culture) => value switch
    {
        Theme.dark => "Тёмная",
        Theme.light => "Светлая",
        Theme.system => "Системная",
        _ => value.ToString()
    };
}