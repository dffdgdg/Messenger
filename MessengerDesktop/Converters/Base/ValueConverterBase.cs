using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Base
{
    public abstract class ValueConverterBase : IValueConverter
    {
        public virtual object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                return ConvertInternal(value, targetType, parameter, culture);
            }
            catch
            {
                return HandleConversionError(value);
            }
        }

        public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                return ConvertBackInternal(value, targetType, parameter, culture);
            }
            catch
            {
                return HandleConversionError(value);
            }
        }

        protected abstract object? ConvertInternal(object? value, Type targetType, object? parameter, CultureInfo culture);

        protected virtual object? ConvertBackInternal(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        protected virtual object? HandleConversionError(object? value) => null;
    }
}