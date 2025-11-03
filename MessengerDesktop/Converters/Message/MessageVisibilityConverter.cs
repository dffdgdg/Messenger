using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MessengerDesktop.Converters.Message
{
    public class MessageSenderVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count != 2 || values[0] is not bool isPrevSameSender || values[1] is not System.DateTime currentMessageTime)
                return true;

            // Получаем время предыдущего сообщения
            var prevMessageTime = values.Count > 2 && values[2] is System.DateTime prev ? prev : System.DateTime.MinValue;

            // Если сообщения от разных отправителей, всегда показываем имя
            if (!isPrevSameSender)
                return true;

            // Проверяем разницу во времени между сообщениями
            var timeDiff = currentMessageTime - prevMessageTime;

            // Если прошло больше 5 минут, показываем имя отправителя
            return timeDiff.TotalMinutes > 5;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}