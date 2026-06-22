using Core.Services.Abstractions;

namespace Core.ViewModels.ChatList.Factories;

/// <summary>
/// Сервисы голосовых звонков внутри чата.
/// </summary>
public sealed class CallServices(ICallService callService, ICallHubConnection callHub)
{
    public ICallService CallService { get; } = callService;
    public ICallHubConnection CallHub { get; } = callHub;
}