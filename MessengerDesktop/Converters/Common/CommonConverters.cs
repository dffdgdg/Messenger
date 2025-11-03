using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Common
{
    public class IndexToTextConverter : ValueConverterBase
    {
        protected override object? ConvertInternal(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int index && parameter is string options)
         {
    var parts = options.Split('|');
              if (index >= 0 && index < parts.Length)
 {
  return parts[index];
           }
      }
     return string.Empty;
      }
    }

    public class EqualityConverter : ValueConverterBase
    {
 protected override object? ConvertInternal(object? value, Type targetType, object? parameter, CultureInfo culture)
     {
        if (parameter != null && value != null)
            {
     if (int.TryParse(parameter.ToString(), out int paramValue) && 
         int.TryParse(value.ToString(), out int actualValue))
         {
         return paramValue == actualValue;
     }
          return value.Equals(parameter);
            }
    return false;
}
    }

    public class StringNotNullOrEmptyConverter : ValueConverterBase
    {
        protected override object? ConvertInternal(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
  return !string.IsNullOrEmpty(value as string);
     }
    }
}