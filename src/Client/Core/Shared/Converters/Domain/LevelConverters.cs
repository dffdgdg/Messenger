using Core.Shared.Converters.Base;
using System.Globalization;

namespace Core.Shared.Converters.Domain;

public class LevelToMarginConverter : ConverterBase<int, Thickness>
{
    public int IndentationPerLevel { get; set; } = 20;

    protected override Thickness ConvertCore(int level, object? parameter, CultureInfo culture) => new(level * IndentationPerLevel, 0, 0, 0);
}

public class LevelToVisibilityConverter : ConverterBase<int, bool>
{
    protected override bool ConvertCore(int level, object? parameter, CultureInfo culture) => level > 0;
}