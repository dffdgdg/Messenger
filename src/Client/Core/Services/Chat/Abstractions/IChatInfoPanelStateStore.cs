namespace Core.Services.Chat.Abstractions;

public interface IChatInfoPanelStateStore
{
    bool IsOpen { get; set; }
}