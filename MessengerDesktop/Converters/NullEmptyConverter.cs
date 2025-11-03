using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class NullEmptyConverter : IValueConverter
    {
        public object? WhenNull { get; set; }
        public object? WhenEmpty { get; set; }
        public object? WhenHasValue { get; set; }
        public bool CheckEmptyString { get; set; } = true;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return WhenNull;

            if (CheckEmptyString && value is string str && string.IsNullOrWhiteSpace(str))
                return WhenEmpty;

            return WhenHasValue ?? value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (WhenHasValue != null && value?.Equals(WhenHasValue) == true)
                return parameter; 

            return null;
        }
    }
}