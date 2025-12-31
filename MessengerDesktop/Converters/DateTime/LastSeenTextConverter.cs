using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MessengerDesktop.Converters.DateTime;

public class LastSeenTextConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return "";

        var isOnline = values[0] is true;
        var lastOnline = values[1] as System.DateTime?;

        if (isOnline) return "в сети";
        if (!lastOnline.HasValue) return "";

        var diff = System.DateTime.Now - lastOnline.Value;

        return diff.TotalMinutes switch
        {
            < 1 => "был(а) только что",
            < 60 => $"был(а) {(int)diff.TotalMinutes} мин. назад",
            < 1440 => $"был(а) {(int)diff.TotalHours} ч. назад",
            < 2880 => "был(а) вчера",
            < 10080 => $"был(а) {(int)diff.TotalDays} дн. назад",
            _ => $"был(а) {lastOnline.Value:dd.MM.yyyy}"
        };
    }
}