using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Desktop.Services.Platform.OS;

public class PlatformService : IPlatformService
{
    private Window? _mainWindow;

    public Window? MainWindow => _mainWindow ?? GetMainWindowFromLifetime();

    public IClipboard? Clipboard => MainWindow?.Clipboard;

    public void Initialize(Window mainWindow) => _mainWindow = mainWindow
        ?? throw new ArgumentNullException(nameof(mainWindow));

    public void Cleanup() => _mainWindow = null;

    public bool IsClipboardAvailable() => MainWindow?.Clipboard is not null;

    public async Task<bool> CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            var clipboard = MainWindow?.Clipboard;
            if (clipboard is null)
            {
                Debug.WriteLine("Буфер обмена недоступен");
                return false;
            }

            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка копирования в буфер обмена: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetFromClipboardAsync()
    {
        try
        {
            var clipboard = MainWindow?.Clipboard;
            if (clipboard is null)
            {
                Debug.WriteLine("Буфер обмена недоступен");
                return null;
            }

            return await clipboard.TryGetTextAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка чтения из буфера обмена: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ClearClipboardAsync()
    {
        try
        {
            var clipboard = MainWindow?.Clipboard;
            if (clipboard is null)
            {
                Debug.WriteLine("Буфер обмена недоступен");
                return false;
            }

            await clipboard.ClearAsync();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка очистки буфера обмена: {ex.Message}");
            return false;
        }
    }

    private static Window? GetMainWindowFromLifetime()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}