using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.DateTime
{
    public class DateTimeConverter : IValueConverter
    {
        public string Format { get; set; } = "default";
        public bool UseRelativeTime { get; set; } = true;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not System.DateTime dateTime || dateTime == default)
                return string.Empty;

            culture ??= new CultureInfo("ru-RU");

            return Format.ToLowerInvariant() switch
            {
                "time" => dateTime.ToString("HH:mm", culture),
                "date" => dateTime.ToString("dd.MM.yyyy", culture),
                "shortdate" => dateTime.ToString("dd.MM", culture),
                "datetime" => dateTime.ToString("dd.MM.yyyy HH:mm", culture),
                "chat" => GetChatTime(dateTime, culture),
                "relative" => GetRelativeTime(dateTime, culture),
                _ => UseRelativeTime ? GetChatTime(dateTime, culture) : dateTime.ToString("dd.MM.yyyy HH:mm", culture)
            };
        }

        private static string GetChatTime(System.DateTime dateTime, CultureInfo culture)
        {
            var now = System.DateTime.Now;
            return dateTime.Date switch
            {
                var date when date == now.Date => dateTime.ToString("HH:mm", culture),
                var date when date == now.Date.AddDays(-1) => "Вчера",
                var date when date.Year == now.Year => dateTime.ToString("d MMMM", culture),
                _ => dateTime.ToString("d MMMM yyyy", culture)
            };
        }

        private static string GetRelativeTime(System.DateTime dateTime, CultureInfo culture)
        {
            var now = System.DateTime.Now;
            var diff = now - dateTime;

            if (diff.TotalMinutes < 1) return "только что";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} мин назад";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} ч назад";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} д назад";

            return dateTime.ToString("dd.MM.yy", culture);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ConvertBack is not supported for DateTimeConverter");
        }
    }
}