using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Base
{
    public abstract class ValueConverterBase<TIn, TOut> : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is TIn typedValue)
            {
                try
                {
                    return ConvertValue(typedValue, parameter, culture);
                }
                catch (Exception)
                {
                    return HandleConversionError(value);
                }
            }

            return HandleInvalidInput(value);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is TOut typedValue && CanConvertBack)
            {
                try
                {
                    return ConvertBackValue(typedValue, parameter, culture);
                }
                catch (Exception)
                {
                    return HandleConversionError(value);
                }
            }

            return HandleInvalidInput(value);
        }

        protected abstract TOut ConvertValue(TIn value, object? parameter, CultureInfo culture);

        protected virtual TIn ConvertBackValue(TOut value, object? parameter, CultureInfo culture) =>
            throw new NotImplementedException();

        protected virtual bool CanConvertBack => false;

        protected virtual object? HandleConversionError(object? value) => null;

        protected virtual object? HandleInvalidInput(object? value) => null;
    }
}