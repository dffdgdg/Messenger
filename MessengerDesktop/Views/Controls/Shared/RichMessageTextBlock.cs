using Avalonia.Controls.Documents;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;

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

    public static readonly StyledProperty<ICommand?> MentionClickCommandProperty =
        AvaloniaProperty.Register<RichMessageTextBlock, ICommand?>(nameof(MentionClickCommand));

    public ICommand? MentionClickCommand
    {
        get => GetValue(MentionClickCommandProperty);
        set => SetValue(MentionClickCommandProperty, value);
    }

    private static readonly Regex UrlRegex = new(@"(https?://[^\s<>""')\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static readonly SolidColorBrush LinkBrush = new(Color.Parse("#4A9EEA"));
    private static readonly SolidColorBrush MentionBrush = new(Color.Parse("#8F7DFF"));
    private static readonly Regex MentionRegex = new("(?<![A-Za-z0-9_])@[A-Za-z0-9_]{3,30}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly List<(int start, int end, string url)> _linkRanges = [];
    private readonly List<(int start, int end, string mention)> _mentionRanges = [];
    static RichMessageTextBlock() => RawTextProperty.Changed.AddClassHandler<RichMessageTextBlock>((ctrl, _) => ctrl.RebuildInlines());

    private void RebuildInlines()
    {
        Inlines?.Clear();
        _linkRanges.Clear();
        _mentionRanges.Clear();

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
                run.SetValue(ForegroundProperty, LinkBrush);
                run.SetValue(TextDecorationsProperty, underline);
            }
            else
            {
                run.SetValue(ForegroundProperty, MentionBrush);
                run.SetValue(FontWeightProperty, FontWeight.SemiBold);
            }
            Inlines.Add(run);

            charPos += token.Length;
            if (Kind == "url")
                _linkRanges.Add((linkStart, charPos, token));
            else
                _mentionRanges.Add((linkStart, charPos, token));

            lastIndex = End;
        }

        if (lastIndex < text.Length)
            Inlines.Add(new Run(text[lastIndex..]));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var url = GetUrlUnderPointer(e);
            if (url != null)
            {
                OpenUrl(url);
                e.Handled = true;
                return;
            }

            var mention = GetMentionUnderPointer(e);
            if (mention != null && MentionClickCommand?.CanExecute(mention) == true)
            {
                MentionClickCommand.Execute(mention);
                e.Handled = true;
                return;
            }
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_linkRanges.Count == 0 && _mentionRanges.Count == 0)
        {
            Cursor = Cursor.Default;
            return;
        }

        Cursor = GetUrlUnderPointer(e) != null || GetMentionUnderPointer(e) != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = Cursor.Default;
    }

    private string? GetUrlUnderPointer(PointerEventArgs e)
    {
        var charIndex = GetCharacterIndexSafe(e);
        if (charIndex < 0)
            return null;

        foreach (var (start, end, url) in _linkRanges)
        {
            if (charIndex >= start && charIndex < end)
                return url;
        }

        return null;
    }
    private int GetCharacterIndexSafe(PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        var textLayout = TextLayout;
        if (textLayout is null) return -1;

        try
        {
            var hitResult = textLayout.HitTestPoint(pos);
            return hitResult.TextPosition;
        }
        catch
        {
            return -1;
        }
    }

    private string? GetMentionUnderPointer(PointerEventArgs e)
    {
        var charIndex = GetCharacterIndexSafe(e);

        if (charIndex < 0)
            return null;

        foreach (var (start, end, mention) in _mentionRanges)
        {
            if (charIndex >= start && charIndex < end)
                return mention;
        }

        return null;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}