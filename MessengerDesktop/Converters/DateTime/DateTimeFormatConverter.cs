using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.DateTime;

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

public class DateTimeFormatConverter : ConverterBase<System.DateTime, string>
{
    private static readonly CultureInfo RuCulture = new("ru-RU");

    public DateTimeFormat Format { get; set; } = DateTimeFormat.Default;
    public string? CustomFormat { get; set; }

    protected override string? DefaultValue => string.Empty;

    protected override string ConvertCore(System.DateTime value, object? parameter, CultureInfo culture)
    {
        if (value == default) return string.Empty;

        if (parameter is string formatStr && System.Enum.TryParse<DateTimeFormat>(formatStr, true, out var format))
            return FormatDateTime(value, format);

        if (!string.IsNullOrEmpty(CustomFormat))
            return value.ToString(CustomFormat, RuCulture);

        return FormatDateTime(value, Format);
    }

    private static string FormatDateTime(System.DateTime dt, DateTimeFormat format) => format switch
    {
        DateTimeFormat.Time => dt.ToString("HH:mm", RuCulture),
        DateTimeFormat.Date => dt.ToString("dd.MM.yyyy", RuCulture),
        DateTimeFormat.ShortDate => dt.ToString("dd.MM", RuCulture),
        DateTimeFormat.DateTime => dt.ToString("dd.MM.yyyy HH:mm", RuCulture),
        DateTimeFormat.Chat => FormatChatTime(dt),
        DateTimeFormat.Relative => FormatRelativeTime(dt),
        _ => dt.ToString("dd.MM.yyyy HH:mm", RuCulture)
    };

    private static string FormatChatTime(System.DateTime dt)
    {
        var now = System.DateTime.Now;

        if (dt.Date == now.Date) return dt.ToString("HH:mm", RuCulture);

        if (dt.Date == now.Date.AddDays(-1)) return "Вчера";

        if (dt.Year == now.Year) return dt.ToString("d MMMM", RuCulture);

        return dt.ToString("d MMMM yyyy", RuCulture);
    }

    private static string FormatRelativeTime(System.DateTime dt)
    {
        var diff = System.DateTime.Now - dt;

        return diff.TotalMinutes switch
        {
            < 1 => "только что",
            < 60 => $"{(int)diff.TotalMinutes} мин назад",
            < 1440 => $"{(int)diff.TotalHours} ч назад",
            < 10080 => $"{(int)diff.TotalDays} д назад",
            _ => dt.ToString("dd.MM.yy", RuCulture)
        };
    }
}