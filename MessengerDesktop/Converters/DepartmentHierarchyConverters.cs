using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class IntToMarginConverter : IValueConverter
    {
        public static readonly IntToMarginConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int parentId && parentId != 0)
            {
                return 20;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IntToVisibilityConverter : IValueConverter
    {
        public static readonly IntToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int parentId)
            {
                // Show tree line only for items with parent
                return parentId != 0;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}