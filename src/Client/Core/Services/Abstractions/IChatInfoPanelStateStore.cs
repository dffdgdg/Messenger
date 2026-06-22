namespace Core.Services.Abstractions;

public interface IChatInfoPanelStateStore
{
    bool IsOpen { get; set; }
}