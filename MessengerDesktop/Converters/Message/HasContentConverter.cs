using Avalonia.Data.Converters;
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;

namespace MessengerDesktop.Converters.Message
{
    public class HasContentConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
                return !string.IsNullOrWhiteSpace(str);

            if (value is ICollection collection)
                return collection.Count > 0;

            Debug.WriteLine($"[HasContentConverter] Unknown type: {value?.GetType().Name}, Value: {value}");
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) 
            => throw new NotSupportedException();
    }
}
