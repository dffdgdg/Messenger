using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media.TextFormatting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MessengerDesktop.Views.Controls.Shared;

public class RichMessageTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> RawTextProperty =
        AvaloniaProperty.Register<RichMessageTextBlock, string?>(nameof(RawText));

    public string? RawText
    {
        get => GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    private static readonly Regex UrlRegex = new(@"(https?://[^\s<>""')\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static readonly SolidColorBrush LinkBrush = new(Color.Parse("#4A9EEA"));
    private static readonly SolidColorBrush MentionBrush = new(Color.Parse("#8F7DFF"));
    private static readonly Regex MentionRegex = new(@"(?<![A-Za-z0-9_])@[A-Za-z0-9_]{3,30}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly List<(int start, int end, string url)> _linkRanges = [];

    static RichMessageTextBlock() => RawTextProperty.Changed.AddClassHandler<RichMessageTextBlock>((ctrl, _) => ctrl.RebuildInlines());

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

        var matches = new List<(int Start, int End, string Kind, string Value)>();
        foreach (Match match in UrlRegex.Matches(text))
            matches.Add((match.Index, match.Index + match.Length, "url", match.Value));
        foreach (Match match in MentionRegex.Matches(text))
            matches.Add((match.Index, match.Index + match.Length, "mention", match.Value));


        if (matches.Count == 0)
        {
            Text = text;
            return;
        }

        matches.Sort((a, b) => a.Start.CompareTo(b.Start));

        Text = null;
        Inlines ??= [];

        // Создаём underline декорацию
        var underline = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline }
        };

        var lastIndex = 0;
        var charPos = 0;

        foreach (var (Start, End, Kind, Value) in matches)
        {
            if (Start < lastIndex)
                continue;

            if (Start > lastIndex)
            {
                var plain = text[lastIndex..Start];
                Inlines.Add(new Run(plain));
                charPos += plain.Length;
            }

            var token = Value;
            var linkStart = charPos;

            var run = new Run(token);
            if (Kind == "url")
            {
                run.SetValue(Inline.ForegroundProperty, LinkBrush);
                run.SetValue(Inline.TextDecorationsProperty, underline);
            }
            else
            {
                run.SetValue(Inline.ForegroundProperty, MentionBrush);
                run.SetValue(Inline.FontWeightProperty, FontWeight.SemiBold);
            }
            Inlines.Add(run);

            charPos += token.Length;
            if (Kind == "url")
                _linkRanges.Add((linkStart, charPos, token));

            lastIndex = End;
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

        var textLayout = TextLayout;
        if (textLayout is null)
            return null;

        int charIndex;
        try
        {
            charIndex = GetCharacterIndex(pos, textLayout);
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

    private static int GetCharacterIndex(Point pos, TextLayout layout)
    {
        var textPosition = layout.HitTestPoint(pos);

        var type = textPosition.GetType();

        var prop = type.GetProperty("TextPosition")
                   ?? type.GetProperty("CharacterHit")
                   ?? type.GetProperty("Position");

        if (prop != null)
        {
            var val = prop.GetValue(textPosition);
            if (val is int i) return i;
            if (val is CharacterHit ch) return ch.FirstCharacterIndex;
        }

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
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}