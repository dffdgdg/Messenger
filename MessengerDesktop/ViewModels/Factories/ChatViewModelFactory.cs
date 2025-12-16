using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;

namespace MessengerDesktop.ViewModels.Factories;

public class ChatViewModelFactory(
    IApiClientService apiClient,
    IAuthService authService,
    IChatInfoPanelStateStore chatInfoPanelStateStore,
    INotificationService notificationService) : IChatViewModelFactory
{
    public ChatViewModel Create(int chatId, ChatsViewModel parent)
    {
        return new ChatViewModel(
            chatId,
            parent,
            apiClient,
            authService,
            chatInfoPanelStateStore,
            notificationService);
    }
}