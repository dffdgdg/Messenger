using Avalonia.Platform.Storage;

namespace Core.Services.Platform.Abstractions;

public interface IPlatformService
{
    Task<bool> CopyToClipboardAsync(string text);
    Task<string?> GetFromClipboardAsync();
    Task<bool> ClearClipboardAsync();
    bool IsClipboardAvailable();
    void Initialize(object? platformContext = null);
    void Cleanup();
    IStorageProvider? GetStorageProvider();
    Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options);
}