using Avalonia.Data.Converters;
using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class ZeroToTrueConverter : ValueConverterBase<int, bool>
    {
        public static readonly ZeroToTrueConverter Instance = new();

        protected override bool ConvertCore(int value, object? parameter, CultureInfo culture)
        {
            return value == 0;
        }

        protected override object? HandleInvalidInput(object? value)
        {
            return false;
        }
    }
}