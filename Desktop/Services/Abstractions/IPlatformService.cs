using Avalonia.Input.Platform;
using System.Threading.Tasks;

namespace Desktop.Services.Abstractions;

public interface IPlatformService
{
    Window? MainWindow { get; }
    IClipboard? Clipboard { get; }
    Task<bool> CopyToClipboardAsync(string text);
    Task<string?> GetFromClipboardAsync();
    Task<bool> ClearClipboardAsync();
    bool IsClipboardAvailable();

    void Initialize(Window mainWindow);
    void Cleanup();
}