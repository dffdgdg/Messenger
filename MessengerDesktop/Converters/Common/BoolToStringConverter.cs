using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Common
{
    public class BoolToStringConverter : ValueConverterBase<bool, string>
    {
        private const char Separator = '|';

        protected override string ConvertValue(bool value, object? parameter, CultureInfo culture)
        {
            if (parameter is not string param)
                return string.Empty;

            var parts = param.Split(Separator);
            if (parts.Length != 2)
                return string.Empty;

            return value ? parts[0] : parts[1];
        }

        protected override object? HandleInvalidInput(object? value) => string.Empty;
        protected override object? HandleConversionError(object? value) => string.Empty;
    }
}