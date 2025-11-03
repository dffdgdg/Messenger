using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Common
{
    public class BooleanToRotateTransformConverter : ValueConverterBase
    {
        public double TrueValue { get; set; } = 90;
        public double FalseValue { get; set; } = 0;

    protected override object? ConvertInternal(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
 if (value is bool boolValue)
  {
       return boolValue ? TrueValue : FalseValue;
   }
    return FalseValue;
        }
    }
}