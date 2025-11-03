using Avalonia.Data.Converters;
using Avalonia.Layout;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Message
{
    public class MessageAlignmentConverter : IValueConverter
    {
        public HorizontalAlignment OwnMessageAlignment { get; set; } = HorizontalAlignment.Right;
        public HorizontalAlignment OtherMessageAlignment { get; set; } = HorizontalAlignment.Left;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool isOwn && isOwn ? OwnMessageAlignment : OtherMessageAlignment;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ConvertBack is not supported for MessageAlignmentConverter");
        }
    }
}