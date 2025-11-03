using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace MessengerDesktop.Services
{
    public static class WindowService
    {
        private static Window? _mainWindow;
        private static IClipboard? _clipboard;

        public static void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow;
            var topLevel = TopLevel.GetTopLevel(mainWindow);
            _clipboard = topLevel?.Clipboard;
        }

        public static Window? GetMainWindow() => _mainWindow;

        public static IClipboard? GetClipboard() => _clipboard;

        public static void Cleanup()
        {
            _mainWindow = null;
            _clipboard = null;
        }
    }
}