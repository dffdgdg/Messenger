using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Generic;

public sealed class ResourceKeyToGeometryConverter : ConverterBase<string, Geometry?>
{
    protected override Geometry? DefaultValue => Geometry.Parse("M4 4h16v16H4z");

    protected override Geometry? ConvertCore(string key, object? parameter, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(key) || Application.Current is null)
            return DefaultValue;

        return Application.Current.TryFindResource(key, out var resource) && resource is Geometry geometry
            ? geometry
            : DefaultValue;
    }
}