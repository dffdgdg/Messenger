using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using System;
using System.Threading.Tasks;

namespace MessengerDesktop.Services
{
    public static class ClipboardService
    {
        /// <summary>
        /// /// Копирует текст в буфер обмена и возвращает результат операции
        /// /// </summary>
        /// /// <param name="text">Текст для копирования</param>
        /// /// <param name="silent">Не показывать уведомления при ошибках</param>
        /// /// <returns>True если копирование успешно, иначе False</returns>
        public static async Task<bool> CopyToClipboard(string text, bool silent = false)
        {
            try
            {
                var clipboard = WindowService.GetClipboard();
                if (clipboard == null)
                    return false;

                await clipboard.SetTextAsync(text);

                if (!silent)
                {
                    NotificationService.ShowSuccess("Текст скопирован в буфер обмена", copyToClipboard: false);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    NotificationService.ShowError(
                            $"Не удалось скопировать текст в буфер обмена: {ex.Message}",
                        copyToClipboard: false);
                }
                return false;
            }
        }

        /// <summary>
        /// Получает текст из буфера обмена
        /// </summary>
        /// <param name="silent">Не показывать уведомления при ошибках</param>
        /// <returns>Текст из буфера обмена или null в случае ошибки</returns>
        public static async Task<string?> GetFromClipboard(bool silent = false)
        {
            try
            {
                var clipboard = WindowService.GetClipboard();
                if (clipboard == null)
                    return null;

                return await clipboard.GetTextAsync();
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    NotificationService.ShowError(
                       $"Не удалось получить текст из буфера обмена: {ex.Message}",
                          copyToClipboard: false);
                }
                return null;
            }
        }

        /// <summary>
        /// Проверяет, доступен ли буфер обмена
        /// </summary>
        public static bool IsClipboardAvailable()
        {
            return WindowService.GetClipboard() != null;
        }

        /// <summary>
        /// Очищает буфер обмена
        /// </summary>
        /// <param name="silent">Не показывать уведомления при ошибках</param>
        public static async Task<bool> ClearClipboard(bool silent = false)
        {
            try
            {
                var clipboard = WindowService.GetClipboard();
                if (clipboard == null)
                    return false;

                await clipboard.ClearAsync();
                return true;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    NotificationService.ShowError(
                      $"Не удалось очистить буфер обмена: {ex.Message}",
                  copyToClipboard: false);
                }
                return false;
            }
        }
    }
}