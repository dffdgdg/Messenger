using Avalonia.Data.Converters;
using Avalonia;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Message
{
    public class PrevSenderToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value - IsPrevSameSender
            if (value is bool isPrevSameSender)
            {
                // Если предыдущее сообщение от того же отправителя - маленький отступ
                return isPrevSameSender
                    ? new Thickness(0, 0, 0, 2)   // компактный отступ
                    : new Thickness(0, 8, 0, 2);   // больший отступ для нового отправителя
            }
            return new Thickness(0, 2, 0, 2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
