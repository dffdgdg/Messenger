using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;

namespace MessengerDesktop.ViewModels.Factories;

public class ChatsViewModelFactory(
    IApiClientService apiClient,
    IAuthService authService,
    IChatViewModelFactory chatViewModelFactory) : IChatsViewModelFactory
{
    public ChatsViewModel Create(MainMenuViewModel parent, bool isGroupMode)
    {
        return new ChatsViewModel(
            parent,
            isGroupMode,
            apiClient,
            authService,
            chatViewModelFactory);
    }
}