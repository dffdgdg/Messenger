using System.Globalization;
using Avalonia;
using MessengerDesktop.Converters.Base;
using MessengerShared.DTO;

namespace MessengerDesktop.Converters.Message
{
    public class MessageToMarginConverter : ValueConverterBase<MessageDTO, Thickness>
    {
        private const double DefaultMargin = 8;
        private const double GroupedMessageMargin = 2;


        protected override Thickness ConvertValue(MessageDTO message, object? parameter, CultureInfo culture)
        {
            if (message.IsOwn) return message.IsPrevSameSender ? new Thickness(0, GroupedMessageMargin, 0, 0) : new Thickness(0, DefaultMargin, 0, 0);
            return message.IsPrevSameSender ? new Thickness(32, GroupedMessageMargin, 0, 0) : new Thickness(32, DefaultMargin, 0, 0);
        }

        protected override object HandleConversionError(object? value) => new Thickness(0);
    }
}