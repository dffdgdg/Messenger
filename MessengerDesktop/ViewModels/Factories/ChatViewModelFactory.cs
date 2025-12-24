using MessengerDesktop.Services;
using MessengerDesktop.Services.Api;
using MessengerDesktop.Services.Auth;
using MessengerDesktop.Services.Platform;
using MessengerDesktop.Services.UI;
using MessengerDesktop.ViewModels.Chat;

namespace MessengerDesktop.ViewModels.Factories;

public interface IChatViewModelFactory
{
    ChatViewModel Create(int chatId, ChatsViewModel parent);
}

public class ChatViewModelFactory(IApiClientService apiClient,IAuthManager authManager,IChatInfoPanelStateStore chatInfoPanelStateStore,
    INotificationService notificationService,IChatNotificationApiService notificationApiService,IGlobalHubConnection globalHub,
    IFileDownloadService fileDownloadService,IPlatformService platformService) : IChatViewModelFactory
{
    public ChatViewModel Create(int chatId, ChatsViewModel parent)
    {
        var storageProvider = platformService.MainWindow?.StorageProvider;

        return new ChatViewModel(
            chatId,
            parent,
            apiClient,
            authManager,
            chatInfoPanelStateStore,
            notificationService,
            notificationApiService,
            globalHub,
            fileDownloadService,
            storageProvider);
    }
}