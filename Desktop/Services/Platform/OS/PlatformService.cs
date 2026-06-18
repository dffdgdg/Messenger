using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using System.Diagnostics;

namespace Desktop.Services.Platform.OS;

public class PlatformService : IPlatformService
{
    private Window? _mainWindow;

    private Window? MainWindow => _mainWindow ?? GetMainWindowFromLifetime();
    private IClipboard? Clipboard => MainWindow?.Clipboard;

    public void Initialize(object? platformContext = null)
    {
        if (platformContext is Window window)
            _mainWindow = window;
    }

    public void Cleanup() => _mainWindow = null;

    public bool IsClipboardAvailable() => MainWindow?.Clipboard is not null;

    public IStorageProvider? GetStorageProvider() => MainWindow?.StorageProvider;

    public async Task<bool> CopyToClipboardAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            var clipboard = Clipboard;
            if (clipboard is null) return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка копирования: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetFromClipboardAsync()
    {
        try
        {
            return await Clipboard?.TryGetTextAsync()!;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка чтения буфера: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ClearClipboardAsync()
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard is null) return false;
            await clipboard.ClearAsync();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка очистки буфера: {ex.Message}");
            return false;
        }
    }

    private static Window? GetMainWindowFromLifetime()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
    {
        throw new NotImplementedException();
    }
}