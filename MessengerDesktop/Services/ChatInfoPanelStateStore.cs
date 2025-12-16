namespace MessengerDesktop.Services
{
    public interface IChatInfoPanelStateStore
    {
        bool IsOpen { get; set; }
        void Remove();
    }

    public class ChatInfoPanelStateStore(Services.Storage.ISettingsService settingsService) : IChatInfoPanelStateStore
    {
        private const string StorageKey = "ChatInfoPanelIsOpen";
        private bool? _cachedValue;

        public bool IsOpen
        {
            get
            {
                if (_cachedValue.HasValue)
                    return _cachedValue.Value;

                _cachedValue = settingsService.Get<bool>(StorageKey);
                return _cachedValue ?? false;
            }
            set
            {
                if (_cachedValue == value)
                    return;

                _cachedValue = value;
                settingsService.Set(StorageKey, value);
            }
        }

        public void Remove()
        {
            _cachedValue = false;
            settingsService.Remove(StorageKey);
        }
    }
}