using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MessengerDesktop.Converters
{
    public class LastSeenTextConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2) return "";

            var isOnline = values[0] is bool online && online;
            var lastOnline = values[1] as System.DateTime?;

            if (isOnline) return "в сети";
            if (!lastOnline.HasValue) return "";

            var diff = System.DateTime.Now - lastOnline.Value;

            if (diff.TotalMinutes < 1) return "был(а) только что";
            if (diff.TotalMinutes < 60) return $"был(а) {(int)diff.TotalMinutes} мин. назад";
            if (diff.TotalHours < 24) return $"был(а) {(int)diff.TotalHours} ч. назад";
            if (diff.TotalDays < 2) return "был(а) вчера";
            if (diff.TotalDays < 7) return $"был(а) {(int)diff.TotalDays} дн. назад";

            return $"был(а) {lastOnline.Value:dd.MM.yyyy}";
        }
    }
}
