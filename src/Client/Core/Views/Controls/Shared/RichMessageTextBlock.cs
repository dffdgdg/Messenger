using Avalonia.Controls.Documents;
using Avalonia.Input;
using Core.Infrastructure.Diagnostics;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace Core.Views.Controls.Shared;

public class RichMessageTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> RawTextProperty =
        AvaloniaProperty.Register<RichMessageTextBlock, string?>(nameof(RawText));

    public static readonly StyledProperty<ICommand?> MentionClickCommandProperty =
        AvaloniaProperty.Register<RichMessageTextBlock, ICommand?>(nameof(MentionClickCommand));

    public string? RawText
    {
        get => GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    public ICommand? MentionClickCommand
    {
        get => GetValue(MentionClickCommandProperty);
        set => SetValue(MentionClickCommandProperty, value);
    }

    private static readonly SolidColorBrush LinkBrush = new(Color.Parse("#4A9EEA"));
    private static readonly SolidColorBrush MentionBrush = new(Color.Parse("#8F7DFF"));
    private static readonly TextDecorationCollection Underline =
    [
        new TextDecoration { Location = TextDecorationLocation.Underline }
    ];

    private static readonly Regex UrlRegex = new(@"(https?://[^\s<>""')\]]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static readonly Regex MentionRegex = new("(?<![A-Za-z0-9_])@[A-Za-z0-9_]{3,30}", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly List<(int start, int end, string url)> _linkRanges = [];
    private readonly List<(int start, int end, string mention)> _mentionRanges = [];
    private string? _lastBuiltText;

    static RichMessageTextBlock()
        => RawTextProperty.Changed.AddClassHandler<RichMessageTextBlock>((ctrl, _) => ctrl.RebuildInlines());

    public RichMessageTextBlock()
        => MemoryDiagnostics.OnRichTextCreated();

    private void RebuildInlines()
    {
        var text = RawText;
        if (text == _lastBuiltText) return;
        _lastBuiltText = text;

        Inlines?.Clear();
        _linkRanges.Clear();
        _mentionRanges.Clear();

        if (string.IsNullOrEmpty(text))
        {
            Text = text;
            return;
        }

        var matches = CollectMatches(text);

        if (matches.Count == 0)
        {
            Text = text;
            return;
        }

        Text = null;
        Inlines ??= [];

        var lastIndex = 0;
        var charPos = 0;

        foreach (var (Start, End, Kind, Value) in matches)
        {
            if (Start < lastIndex) continue;

            if (Start > lastIndex)
            {
                var plain = text[lastIndex..Start];
                Inlines.Add(new Run(plain));
                charPos += plain.Length;
            }

            var run = new Run(Value);
            if (Kind == "url")
            {
                run.Foreground = LinkBrush;
                run.TextDecorations = Underline;
                _linkRanges.Add((charPos, charPos + Value.Length, Value));
            }
            else
            {
                run.Foreground = MentionBrush;
                run.FontWeight = FontWeight.SemiBold;
                _mentionRanges.Add((charPos, charPos + Value.Length, Value));
            }

            Inlines.Add(run);
            charPos += Value.Length;
            lastIndex = End;
        }

        if (lastIndex < text.Length)
            Inlines.Add(new Run(text[lastIndex..]));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        MemoryDiagnostics.OnRichTextDestroyed();
        Inlines?.Clear();
        Text = null;
        _lastBuiltText = null;
        _linkRanges.Clear();
        _mentionRanges.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var charIndex = GetCharIndex(e);

            var url = FindUrl(charIndex);
            if (url != null)
            {
                OpenUrl(url);
                e.Handled = true;
                return;
            }

            var mention = FindMention(charIndex);
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
        var charIndex = GetCharIndex(e);
        var isOverLink = FindUrl(charIndex) != null || FindMention(charIndex) != null;
        Cursor = isOverLink ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = Cursor.Default;
    }

    private int GetCharIndex(PointerEventArgs e)
    {
        try
        {
            var pos = e.GetPosition(this);
            return TextLayout?.HitTestPoint(pos).TextPosition ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    private string? FindUrl(int charIndex)
    {
        if (charIndex < 0) return null;
        foreach (var (start, end, url) in _linkRanges)
            if (charIndex >= start && charIndex < end) return url;
        return null;
    }

    private string? FindMention(int charIndex)
    {
        if (charIndex < 0) return null;
        foreach (var (start, end, mention) in _mentionRanges)
            if (charIndex >= start && charIndex < end) return mention;
        return null;
    }

    private static List<(int Start, int End, string Kind, string Value)> CollectMatches(string text)
    {
        var matches = new List<(int, int, string, string)>();

        foreach (Match m in UrlRegex.Matches(text))
            matches.Add((m.Index, m.Index + m.Length, "url", m.Value));
        foreach (Match m in MentionRegex.Matches(text))
            matches.Add((m.Index, m.Index + m.Length, "mention", m.Value));

        matches.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return matches;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }
}