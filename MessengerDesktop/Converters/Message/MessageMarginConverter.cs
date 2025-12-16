using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Message
{
    public class MessageMarginConverter : IValueConverter
    {
        public Thickness OwnMessageMargin { get; set; } = new Thickness(60, 2, 10, 2);
        public Thickness OtherMessageMargin { get; set; } = new Thickness(10, 2, 60, 2);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not bool isOwn) return new Thickness(10, 2, 10, 2);

            return isOwn ? OwnMessageMargin : OtherMessageMargin;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ConvertBack is not supported for MessageMarginConverter");
        }
    }
}