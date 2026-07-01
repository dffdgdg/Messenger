using Core.Services.Call.Abstractions;

namespace Core.Features.ChatList.ViewModels.Factories;

/// <summary>
/// Сервисы голосовых звонков внутри чата.
/// </summary>
public sealed class CallServices(ICallService callService, ICallHubConnection callHub)
{
    public ICallService CallService { get; } = callService;
    public ICallHubConnection CallHub { get; } = callHub;
}