using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class StringToBrushConverter : IValueConverter
    {
        public IBrush? WhenEmpty { get; set; }
        public IBrush? WhenHasValue { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (string.IsNullOrEmpty(value as string))
                return WhenEmpty ?? Brushes.Transparent;

            return WhenHasValue ?? Brushes.White;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}