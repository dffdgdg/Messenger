using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MessengerDesktop.Services;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace MessengerDesktop.Converters
{
    public class AvatarConverter(IApiClientService apiClientService) : IValueConverter
    {
        private readonly IApiClientService _apiClientService = apiClientService ?? throw new ArgumentNullException(nameof(apiClientService));
        private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? avatarUrl = value as string;

            var placeholder = LoadDefaultAvatar();

            if (string.IsNullOrWhiteSpace(avatarUrl))
                return placeholder;

            if (_cache.TryGetValue(avatarUrl, out var cached))
                return cached;

            // Асинхронно загружаем картинку
            _ = LoadBitmapAsync(avatarUrl);

            return placeholder;
        }

        private async Task LoadBitmapAsync(string avatarUrl)
        {
            try
            {
                using var stream = await _apiClientService.GetStreamAsync(avatarUrl);
                if (stream == null) return;

                var bitmap = await Task.Run(() => Bitmap.DecodeToWidth(stream, 40));
                _cache[avatarUrl] = bitmap;

                // Обновляем UI на главном потоке
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // В Avalonia нельзя напрямую вызвать PropertyChanged у конвертера,
                    // поэтому элемент, который привязан к конвертеру, должен использовать INotifyPropertyChanged
                    // и при смене Source заново дергать Convert. Обычно через ObservableObject или ReactiveUI.
                });
            }
            catch
            {
                // Игнорируем ошибки, оставляем placeholder
            }
        }

        private static Bitmap LoadDefaultAvatar()
        {
            Stream? stream = AssetLoader.Open(new Uri("avares://MessengerDesktop/Assets/Images/default-avatar.webp"));
            if (stream != null)
                return new Bitmap(stream);

            throw new FileNotFoundException("Default avatar resource not found");
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
