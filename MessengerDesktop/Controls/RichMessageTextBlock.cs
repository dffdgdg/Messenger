using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MessengerDesktop.Controls;

public class RichMessageTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> RawTextProperty =
        AvaloniaProperty.Register<RichMessageTextBlock, string?>(nameof(RawText));

    public string? RawText
    {
        get => GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    private static readonly Regex UrlRegex = new(
    @"(https?://[^\s<>""')\]]+)",
    RegexOptions.Compiled | RegexOptions.IgnoreCase,
    TimeSpan.FromSeconds(1));

    private static readonly SolidColorBrush LinkBrush = new(Color.Parse("#4A9EEA"));

    private readonly List<(int start, int end, string url)> _linkRanges = [];

    static RichMessageTextBlock()
    {
        RawTextProperty.Changed.AddClassHandler<RichMessageTextBlock>(
            (ctrl, _) => ctrl.RebuildInlines());
    }

    private void RebuildInlines()
    {
        Inlines?.Clear();
        _linkRanges.Clear();

        var text = RawText;
        if (string.IsNullOrEmpty(text))
        {
            Text = text;
            return;
        }

        var matches = UrlRegex.Matches(text);

        if (matches.Count == 0)
        {
            Text = text;
            return;
        }

        Text = null;
        Inlines ??= [];

        // Создаём underline декорацию
        var underline = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline }
        };

        var lastIndex = 0;
        var charPos = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                var plain = text[lastIndex..match.Index];
                Inlines.Add(new Run(plain));
                charPos += plain.Length;
            }

            var url = match.Value;
            var linkStart = charPos;

            var run = new Run(url);
            run.SetValue(Inline.ForegroundProperty, LinkBrush);
            run.SetValue(Inline.TextDecorationsProperty, underline);
            Inlines.Add(run);

            charPos += url.Length;
            _linkRanges.Add((linkStart, charPos, url));

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            Inlines.Add(new Run(text[lastIndex..]));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_linkRanges.Count > 0 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var url = GetUrlUnderPointer(e);
            if (url != null)
            {
                OpenUrl(url);
                e.Handled = true;
                return;
            }
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_linkRanges.Count == 0)
        {
            Cursor = Cursor.Default;
            return;
        }

        Cursor = GetUrlUnderPointer(e) != null
            ? new Cursor(StandardCursorType.Hand)
            : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = Cursor.Default;
    }

    private string? GetUrlUnderPointer(PointerEventArgs e)
    {
        var pos = e.GetPosition(this);

        // В Avalonia 11.3 используем TextLayout.HitTestPoint
        var textLayout = TextLayout;
        if (textLayout is null)
            return null;

        var hit = textLayout.HitTestPoint(pos);

        // hit — это TextHitTestResult, получаем индекс символа
        int charIndex;
        try
        {
            // Avalonia 11.3: HitTestPoint возвращает TextHitTestResult
            // Пробуем получить позицию через рефлексию если прямой доступ не работает
            charIndex = GetCharacterIndex(hit, pos, textLayout);
        }
        catch
        {
            return null;
        }

        if (charIndex < 0)
            return null;

        foreach (var (start, end, url) in _linkRanges)
        {
            if (charIndex >= start && charIndex < end)
                return url;
        }

        return null;
    }

    private static int GetCharacterIndex(TextHitTestResult hit, Point pos, TextLayout layout)
    {
        // Avalonia 11.3: TextHitTestResult имеет IsInside и IsTrailing
        // Индекс получаем через HitTestTextPosition
        var textPosition = layout.HitTestPoint(pos);

        // Используем свойства напрямую через dynamic для совместимости
        var type = textPosition.GetType();

        // Пробуем разные имена свойств
        var prop = type.GetProperty("TextPosition")
                   ?? type.GetProperty("CharacterHit")
                   ?? type.GetProperty("Position");

        if (prop != null)
        {
            var val = prop.GetValue(textPosition);
            if (val is int i) return i;
            if (val is CharacterHit ch) return ch.FirstCharacterIndex;
        }

        // Fallback: вычисляем позицию по X координатам строк
        return GetCharIndexByPosition(layout, pos);
    }

    private static int GetCharIndexByPosition(TextLayout layout, Point pos)
    {
        var lines = layout.TextLines;
        var y = 0.0;
        var globalIndex = 0;

        foreach (var line in lines)
        {
            if (pos.Y >= y && pos.Y < y + line.Height)
            {
                // Нашли строку, ищем символ по X
                var lineHit = line.GetCharacterHitFromDistance(pos.X);
                return globalIndex + lineHit.FirstCharacterIndex;
            }

            y += line.Height;
            globalIndex += line.Length;
        }

        return -1;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}