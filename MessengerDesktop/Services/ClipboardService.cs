using Avalonia;
using Avalonia.Input.Platform;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public static class ClipboardService
    {
        public static async Task<bool> CopyToClipboard(string text, bool silent = false)
        {
            try
            {
                var clipboard = GetClipboard();
                if (clipboard == null)
                {
                    if (!silent)
                        await NotificationService.ShowError("Буфер обмена недоступен", copyToClipboard: false);
                    return false;
                }

                await clipboard.SetTextAsync(text);

                if (!silent)
                    await NotificationService.ShowSuccess("Текст скопирован в буфер обмена", copyToClipboard: false);

                return true;
            }
            catch (Exception ex)
            {
                if (!silent)
                    await NotificationService.ShowError($"Ошибка копирования: {ex.Message}", copyToClipboard: false);
                return false;
            }
        }

        public static async Task<string?> GetFromClipboard(bool silent = false)
        {
            try
            {
                var clipboard = GetClipboard();
                if (clipboard == null)
                {
                    if (!silent)
                        await NotificationService.ShowError("Буфер обмена недоступен", copyToClipboard: false);
                    return null;
                }

                var text = await ClipboardExtensions.TryGetTextAsync(clipboard);
                if (string.IsNullOrEmpty(text))
                {
                    if (!silent)
                        await NotificationService.ShowWarning("Буфер обмена пуст или не содержит текст", copyToClipboard: false);
                    return null;
                }

                return text;
            }
            catch (Exception ex)
            {
                if (!silent)
                    await NotificationService.ShowError($"Ошибка чтения буфера обмена: {ex.Message}", copyToClipboard: false);
                return null;
            }
        }

        public static async Task<bool> ClearClipboard(bool silent = false)
        {
            try
            {
                var clipboard = GetClipboard();
                if (clipboard == null)
                {
                    if (!silent)
                        await NotificationService.ShowError("Буфер обмена недоступен", copyToClipboard: false);
                    return false;
                }

                await clipboard.ClearAsync();
                return true;
            }
            catch (Exception ex)
            {
                if (!silent)
                    await NotificationService.ShowError($"Ошибка очистки: {ex.Message}", copyToClipboard: false);
                return false;
            }
        }

        public static bool IsClipboardAvailable() => GetClipboard() != null;

        private static IClipboard? GetClipboard()
        {
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow?.Clipboard;
            }

            return null;
        }
    }
}
