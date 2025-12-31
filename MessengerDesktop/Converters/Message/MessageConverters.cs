using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using MessengerDesktop.Converters.Base;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace MessengerDesktop.Converters.Message;

public class MessageAlignmentConverter : ConverterBase<bool, HorizontalAlignment>
{
    public HorizontalAlignment OwnMessageAlignment { get; set; } = HorizontalAlignment.Right;
    public HorizontalAlignment OtherMessageAlignment { get; set; } = HorizontalAlignment.Left;

    protected override HorizontalAlignment ConvertCore(bool isOwn, object? parameter, CultureInfo culture)
        => isOwn ? OwnMessageAlignment : OtherMessageAlignment;
}

public class MessageMarginConverter : ConverterBase<bool, Thickness>
{
    public Thickness OwnMessageMargin { get; set; } = new(60, 2, 10, 2);
    public Thickness OtherMessageMargin { get; set; } = new(10, 2, 60, 2);

    protected override Thickness DefaultValue => new(10, 2, 10, 2);

    protected override Thickness ConvertCore(bool isOwn, object? parameter, CultureInfo culture)
        => isOwn ? OwnMessageMargin : OtherMessageMargin;
}

public class HasContentConverter : ConverterBase
{
    protected override object? ConvertCore(object? value, object? parameter, CultureInfo culture) => value switch
    {
        string str => !string.IsNullOrWhiteSpace(str),
        ICollection { Count: > 0 } => true,
        _ => false
    };
}

public class HasTextOrAttachmentsMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Count: >= 2 })
            return false;

        var hasText = values[0] is string text && !string.IsNullOrWhiteSpace(text);
        var hasAttachments = values[1] is ICollection { Count: > 0 };

        return hasText || hasAttachments;
    }
}