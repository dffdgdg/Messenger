using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Net.Http;

namespace MessengerDesktop.Converters
{
    public class UrlToImageBrushConverter : IValueConverter
    {
        private static readonly HttpClient client = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string url || string.IsNullOrEmpty(url))
                return new SolidColorBrush(Avalonia.Media.Color.Parse("#444444")); // fallback color

            try
            {
                // Load image from URL
                var bytes = client.GetByteArrayAsync(url).Result;
                using var stream = new System.IO.MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                
                return new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill
                };
            }
            catch
            {
                return new SolidColorBrush(Avalonia.Media.Color.Parse("#444444")); // fallback on error
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}