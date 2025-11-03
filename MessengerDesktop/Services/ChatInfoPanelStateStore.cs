using System.Text.Json;

namespace MessengerDesktop.Services
{
    public static class ChatInfoPanelStateStore
    {
        private const string StorageKey = "ChatInfoPanelIsOpen";
        private static bool _isLoaded = false;
        private static bool _isOpen = false;

        private static void EnsureLoaded()
        {
            if (_isLoaded) return;

            try
            {
                var storage = App.Current.Storage;
                if (storage != null)
                {
                    var obj = storage.Get(StorageKey);
                    if (obj == null) _isOpen = false;
                    else if (obj is bool b) _isOpen = b;
                    else if (obj is JsonElement je)
                    {
                        if (je.ValueKind == JsonValueKind.True) _isOpen = true;
                        else if (je.ValueKind == JsonValueKind.False) _isOpen = false;
                        else _ = bool.TryParse(je.ToString(), out _isOpen);
                    }
                    else _ = bool.TryParse(obj.ToString(), out _isOpen);
                }
            }
            catch
            {
                _isOpen = false;
            }
            finally
            {
                _isLoaded = true;
            }
        }

        public static bool Get()
        {
            EnsureLoaded();
            return _isOpen;
        }

        public static void Set(bool isOpen)
        {
            EnsureLoaded();
            if (_isOpen == isOpen) return;
            _isOpen = isOpen;
            try
            {
                var storage = App.Current.Storage;
                storage?.Set(StorageKey, isOpen);
            }
            catch
            {
            }
        }

        public static void Remove()
        {
            try
            {
                var storage = App.Current.Storage;
                storage?.Remove(StorageKey);
                _isOpen = false;
            }
            catch
            {
            }
        }
    }
}
