using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.UI
{
    public class VisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public bool UseHidden { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isVisible = value switch
            {
                bool b => b,
                string s => !string.IsNullOrEmpty(s),
                null => false,
                _ => true
            };

            if (Invert) isVisible = !isVisible;

            return isVisible;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                bool b => Invert ? !b : b,
                _ => false
            };
        }
    }
}