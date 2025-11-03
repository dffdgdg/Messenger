using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class BoolToStringConverter : IValueConverter
    {
        public static readonly BoolToStringConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string options)
            {
                var parts = options.Split('|');
                return boolValue ? parts[0] : parts[1];
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IndexToTextConverter : IValueConverter
    {
        public static readonly IndexToTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index && parameter is string options)
            {
                var parts = options.Split('|');
                return index < parts.Length ? parts[index] : string.Empty;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EqualityConverter : IValueConverter
    {
        public static readonly EqualityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                return value.ToString() == parameter.ToString();
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringNotNullOrEmptyConverter : IValueConverter
    {
        public static readonly StringNotNullOrEmptyConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string options)
            {
                var parts = options.Split('|');
                return value == null ? parts[0] : parts[1];
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}