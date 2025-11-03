using MessengerDesktop.Converters.Base;
using MessengerDesktop.Converters.Constants;
using System.Globalization;

namespace MessengerDesktop.Converters.DateTime
{
    public class DateTimeToDisplayTextConverter : ValueConverterBase<System.DateTime, string>
    {
        private static readonly CultureInfo RussianCulture = new("ru-RU");

        protected override string ConvertValue(System.DateTime messageDate, object? parameter, CultureInfo culture)
        {
            var now = System.DateTime.Now;

            return messageDate.Date switch
            {
                var date when date == now.Date =>
                    messageDate.ToString(DateTimeFormats.TimeOnly, RussianCulture),

                var date when date == now.Date.AddDays(-1) =>
                    messageDate.ToString(DateTimeFormats.YesterdayWithTime, RussianCulture),

                var date when date.Year == now.Year =>
                    messageDate.ToString(DateTimeFormats.DateWithTime, RussianCulture),

                _ => messageDate.ToString(DateTimeFormats.FullDateWithTime, RussianCulture)
            };
        }

        protected override object? HandleConversionError(object? value)
        {
            return string.Empty;
        }

        protected override object? HandleInvalidInput(object? value)
        {
            return string.Empty;
        }
    }
}