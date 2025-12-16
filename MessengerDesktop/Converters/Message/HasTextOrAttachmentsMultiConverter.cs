using Avalonia.Data.Converters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace MessengerDesktop.Converters.Message
{
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
}
