using Avalonia;
using System;

namespace MessengerDesktop.Converters.Boolean
{
    internal static class BooleanConverterHelpers
    {

        private static object ConvertToThickness(bool boolValue, object? parameter)
        {
            if (parameter?.ToString()?.Contains("message", StringComparison.OrdinalIgnoreCase) == true)
                return boolValue ? new Thickness(0, 8, 0, 0) : new Thickness(32, 8, 0, 0);

            if (parameter is int level)
                return new Thickness(level * 20, 0, 0, 0);

            return new Thickness(0);
        }
    }
}