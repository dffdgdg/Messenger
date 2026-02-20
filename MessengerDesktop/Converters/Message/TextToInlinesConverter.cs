using Avalonia.Controls.Documents;
using Avalonia.Media;
using MessengerDesktop.Converters.Base;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MessengerDesktop.Converters.Message;

public class TextToInlinesConverter : ConverterBase<string, InlineCollection>
{
    public static readonly TextToInlinesConverter Instance = new();

    protected override bool AllowNull => false;

    private static readonly Regex UrlRegex = new(@"(https?://[^\s<>""')\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly SolidColorBrush LinkBrush = new(Color.Parse("#4A9EEA"));

    protected override InlineCollection? ConvertCore(string value, object? parameter, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var inlines = new InlineCollection();
        var lastIndex = 0;

        foreach (Match match in UrlRegex.Matches(value))
        {
            // Обычный текст перед ссылкой
            if (match.Index > lastIndex)
                inlines.Add(new Run(value[lastIndex..match.Index]));

            // Ссылка — синий + подчёркивание
            inlines.Add(new Run(match.Value)
            {
                Foreground = LinkBrush,
                TextDecorations = TextDecorations.Underline
            });

            lastIndex = match.Index + match.Length;
        }

        // Хвост после последней ссылки
        if (lastIndex < value.Length)
            inlines.Add(new Run(value[lastIndex..]));

        // Если ссылок не было — вернуть null, пусть используется обычный Text
        return lastIndex == 0 ? null : inlines;
    }
}