using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Comparison;

public enum ComparisonMode { Equal, NotEqual, GreaterThanZero, Zero }

public sealed class ComparisonConverter : ConverterBase
{
    public ComparisonMode Mode { get; set; } = ComparisonMode.Equal;

    protected override object? ConvertCore(object? value, object? parameter, CultureInfo culture) => Mode switch
    {
        ComparisonMode.Equal => StringEquals(value?.ToString(), parameter?.ToString()),
        ComparisonMode.NotEqual => !StringEquals(value?.ToString(), parameter?.ToString()),
        ComparisonMode.GreaterThanZero => value is int i && i > 0,
        ComparisonMode.Zero => value is int z && z == 0,
        _ => false
    };

    private static bool StringEquals(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}