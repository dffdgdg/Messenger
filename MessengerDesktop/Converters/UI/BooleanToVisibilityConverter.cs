using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.UI
{
    public class BooleanToVisibilityConverter(bool invert = false) : ValueConverterBase<bool, bool>
    {
        protected override bool ConvertValue(bool value, object? parameter, CultureInfo culture) => invert ? !value : value;

        protected override object? HandleConversionError(object? value) => !invert;
    }
}