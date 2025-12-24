using MessengerDesktop.Services.Storage;

namespace MessengerDesktop.Services;

public interface IChatInfoPanelStateStore
{
    bool IsOpen { get; set; }
}

public class ChatInfoPanelStateStore(ISettingsService settingsService) : IChatInfoPanelStateStore
{
    private readonly ISettingsService _settingsService = settingsService ?? throw new System.ArgumentNullException(nameof(settingsService));
    private const string Key = "ChatInfoPanelIsOpen";

    public bool IsOpen
    {
        get => _settingsService.Get<bool?>(Key) ?? false;
        set => _settingsService.Set(Key, value);
    }
}