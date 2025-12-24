using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class ZeroToTrueConverter : ValueConverterBase<int, bool>
    {
        public static readonly ZeroToTrueConverter Instance = new();

        protected override bool ConvertCore(int value, object? parameter, CultureInfo culture) 
            => value == 0;

        protected override object? HandleInvalidInput(object? value) => false;
    }
}