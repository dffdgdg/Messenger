using Avalonia.Markup.Xaml;
using System;

namespace MessengerDesktop.Converters
{
    public class Converter : MarkupExtension
    {
        public string? Name { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Name)) throw new InvalidOperationException("Converter name must be provided.");

            return ConverterLocator.Instance.GetConverterByName(Name);
        }
    }
    public class MultiConverter : MarkupExtension
    {
        public string? Name { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Name))
                throw new InvalidOperationException("Converter name must be provided.");

            return ConverterLocator.Instance.GetMultiConverterByName(Name);
        }
    }
}
