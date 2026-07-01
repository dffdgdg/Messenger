using Core.Data.Repositories.Abstractions;
using Core.Features.Shell.MainMenu;
using Core.Services.Api.Abstraction;
using Core.Services.Auth.Abstractions;
using Core.Services.Realtime.Abstractions;

namespace Core.Features.ChatList.ViewModels.Factories;

public interface IChatListViewModelFactory
{
    ChatListViewModel Create(MainMenuViewModel parent, bool isGroupMode);
}

public class ChatListViewModelFactory(IApiClientService apiClient, IAuthManager authManager, IChatViewModelFactory chatViewModelFactory,
    IGlobalHubConnection globalHub, ILocalCacheService cacheService) : IChatListViewModelFactory
{
    public ChatListViewModel Create(MainMenuViewModel parent, bool isGroupMode) =>
        new(parent, isGroupMode, apiClient, authManager, chatViewModelFactory, globalHub, cacheService);
}