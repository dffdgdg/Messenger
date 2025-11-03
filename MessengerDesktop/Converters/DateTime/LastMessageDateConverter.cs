using System.Globalization;
using MessengerDesktop.Converters.Base;
using MessengerDesktop.Converters.Constants;
using MessengerShared.DTO;

namespace MessengerDesktop.Converters.DateTime
{
    public class LastMessageDateConverter : ValueConverterBase<MessageDTO, string>
    {
        private static readonly CultureInfo RussianCulture = new("ru-RU");

        protected override string ConvertValue(MessageDTO message, object? parameter, CultureInfo culture)
        {
            if (message.CreatedAt == default) return string.Empty;

            var now = System.DateTime.Now;
            var messageDate = message.CreatedAt;

            return messageDate.Date switch
            {
                var date when date == now.Date => messageDate.ToString(DateTimeFormats.TimeOnly, RussianCulture),

                var date when date == now.Date.AddDays(-1) => messageDate.ToString(DateTimeFormats.YesterdayWithTime, RussianCulture),

                var date when date.Year == now.Year => messageDate.ToString(DateTimeFormats.DateWithTime, RussianCulture),

                _ => messageDate.ToString(DateTimeFormats.FullDateWithTime, RussianCulture)
            };
        }

        protected override object? HandleConversionError(object? value) => string.Empty;
    }
}