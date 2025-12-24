using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Hierarchy
{
    public class LevelToMarginConverter : IValueConverter
    {
        public static readonly LevelToMarginConverter Instance = new();

        private const int IndentationPerLevel = 20;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                return new Thickness(level * IndentationPerLevel, 0, 0, 0);
            }
            return new Thickness(0);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) 
            => throw new NotImplementedException();
    }
}