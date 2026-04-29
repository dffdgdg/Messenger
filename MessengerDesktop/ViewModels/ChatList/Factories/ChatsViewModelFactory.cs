using MessengerDesktop.Data.Repositories.Abstractions;

namespace MessengerDesktop.ViewModels.ChatList.Factories;

public interface IChatsViewModelFactory
{
    ChatsViewModel Create(MainMenuViewModel parent, bool isGroupMode);
}

public class ChatsViewModelFactory(IApiClientService apiClient, IAuthManager authManager, IChatViewModelFactory chatViewModelFactory,
    IGlobalHubConnection globalHub, ILocalCacheService cacheService) : IChatsViewModelFactory
{
    public ChatsViewModel Create(MainMenuViewModel parent, bool isGroupMode) =>
        new(parent, isGroupMode, apiClient, authManager, chatViewModelFactory, globalHub, cacheService);
}