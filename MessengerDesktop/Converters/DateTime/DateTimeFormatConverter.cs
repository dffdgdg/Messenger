using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.DateTime;

public class DateTimeFormatConverter : ValueConverterBase<System.DateTime, string>
{
    private static readonly CultureInfo DefaultCulture = new("ru-RU");

    public DateTimeFormat Format { get; set; } = DateTimeFormat.Default;
    public string? CustomFormat { get; set; }

    protected override string ConvertCore(System.DateTime value, object? parameter, CultureInfo culture)
    {
        if (value == default) return string.Empty;

        culture = DefaultCulture;

        if (parameter is string formatStr && System.Enum.TryParse<DateTimeFormat>(formatStr, true, out var format))
            return FormatDateTime(value, format, culture);

        if (!string.IsNullOrEmpty(CustomFormat))
            return value.ToString(CustomFormat, culture);

        return FormatDateTime(value, Format, culture);
    }

    private static string FormatDateTime(System.DateTime dateTime, DateTimeFormat format, CultureInfo culture)
    {
        return format switch
        {
            DateTimeFormat.Time => dateTime.ToString("HH:mm", culture),
            DateTimeFormat.Date => dateTime.ToString("dd.MM.yyyy", culture),
            DateTimeFormat.ShortDate => dateTime.ToString("dd.MM", culture),
            DateTimeFormat.DateTime => dateTime.ToString("dd.MM.yyyy HH:mm", culture),
            DateTimeFormat.Chat => FormatChatTime(dateTime, culture),
            DateTimeFormat.Relative => FormatRelativeTime(dateTime),
            _ => dateTime.ToString("dd.MM.yyyy HH:mm", culture)
        };
    }

    private static string FormatChatTime(System.DateTime dateTime, CultureInfo culture)
    {
        var now = System.DateTime.Now;

        if (dateTime.Date == now.Date)
            return dateTime.ToString("HH:mm", culture);

        if (dateTime.Date == now.Date.AddDays(-1))
            return "Вчера";

        if (dateTime.Year == now.Year)
            return dateTime.ToString("d MMMM", culture);

        return dateTime.ToString("d MMMM yyyy", culture);
    }

    private static string FormatRelativeTime(System.DateTime dateTime)
    {
        var diff = System.DateTime.Now - dateTime;

        return diff.TotalMinutes switch
        {
            < 1 => "только что",
            < 60 => $"{(int)diff.TotalMinutes} мин назад",
            < 1440 => $"{(int)diff.TotalHours} ч назад", 
            < 10080 => $"{(int)diff.TotalDays} д назад",
            _ => dateTime.ToString("dd.MM.yy")
        };
    }

    protected override object? HandleInvalidInput(object? value) => string.Empty;
}

public enum DateTimeFormat
{
    Default,
    Time,
    Date,
    ShortDate,
    DateTime,
    Chat,
    Relative
}